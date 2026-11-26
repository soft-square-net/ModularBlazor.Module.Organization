<#
.SYNOPSIS
  The Organization end-to-end demo against the running playground (dotnet run --project playground).

.DESCRIPTION
  Against deployment A (the central service, http://localhost:5081) and deployment B (the connector, through its
  gateway http://localhost:5080), signed in as the dev accounts:

    1. creates (or finds) an org "playground-news" with a sector, a department and a Section with two categories;
    2. adds the manager account as a person, with the position "manager" in the Section;
    3. creates (or finds) the tenant seed "newsdesk" for deployment "branch", rooted at the department, with the
       manager as its admin, and waits until B's seed reconciler has created the tenant and reported Provisioned;
    4. checks B's replica of the tenant (local API): the department and the Section in the tenant, the org and the
       sector as ancestors, the Section's categories, and the managers' grants (source org:{unitId});
    5. renames the Section on the central service and waits until B's replica has it.

  Every step is idempotent: run it again to repeat the checks (step 5 renames the Section again each run).
  Exits with 1 when a check fails. Windows PowerShell 5.1 or PowerShell 7.

  DEVELOPMENT ONLY: it signs in with the playground's dev accounts. The platform admin's password is read from the
  platform checkout's identity appsettings.Development.json; the manager's is the playground's (AppHost.cs).
#>
param(
    [string] $Identity = 'http://localhost:5090/',
    [string] $Central = 'http://localhost:5081/api/organization/',
    [string] $Gateway = 'http://localhost:5080/',
    [string] $PlatformRoot = (Join-Path $PSScriptRoot '..\..\Platform'),
    [string] $AdminEmail = 'admin@modular.local',
    [string] $ManagerEmail = 'manager@modular.local',
    [string] $ManagerPassword = 'Dev-Manager-2026!',
    [string] $Tenant = 'newsdesk',
    [string] $Deployment = 'branch',
    [int] $TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Web
$script:failed = 0

function Step([string] $text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Ok([string] $text) { Write-Host "   ok   $text" -ForegroundColor Green }
function Fail([string] $text) { Write-Host "   FAIL $text" -ForegroundColor Red; $script:failed++ }
function Check([bool] $condition, [string] $text) { if ($condition) { Ok $text } else { Fail $text } }

# ------------------------------------------------------------------ HTTP

# One client per identity session (its cookies); redirects are followed by hand.
function New-Client {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    $script:http = New-Object System.Net.Http.HttpClient($handler)
    $script:http.Timeout = [TimeSpan]::FromSeconds(30)
}
New-Client

function Send([string] $method, [string] $url, $body = $null, [string] $token = $null, [string] $tenant = $null, [switch] $Form, [switch] $AllowFailure) {
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($method), $url)
    if ($token) { $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $token) }
    if ($tenant) { $request.Headers.Add('X-Tenant', $tenant) }
    if ($null -ne $body) {
        if ($Form) {
            $pairs = New-Object 'System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[string,string]]'
            foreach ($k in $body.Keys) { $pairs.Add((New-Object 'System.Collections.Generic.KeyValuePair[string,string]'($k, [string]$body[$k]))) }
            $request.Content = New-Object System.Net.Http.FormUrlEncodedContent(,$pairs)
        } else {
            $json = $body | ConvertTo-Json -Depth 10 -Compress
            $request.Content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, 'application/json')
        }
    }
    $response = $script:http.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $result = [pscustomobject]@{ Status = [int]$response.StatusCode; Location = $response.Headers.Location; Text = $text; Json = $null }
    if ($text -and $response.Content.Headers.ContentType -and $response.Content.Headers.ContentType.MediaType -like '*json*') {
        $result.Json = $text | ConvertFrom-Json
    }
    if (-not $AllowFailure -and $result.Status -ge 400) { throw "$method $url -> $($result.Status): $text" }
    return $result
}

function Absolute([string] $base, $location) {
    if ($location.IsAbsoluteUri) { return $location.AbsoluteUri }
    return (New-Object Uri((New-Object Uri($base)), $location.OriginalString)).AbsoluteUri
}

# ------------------------------------------------------------------ sign-in (authorization code + PKCE, like the Blazor client)

function Get-UserToken([string] $email, [string] $password, [string] $tenant) {
    New-Client   # a fresh identity session per account
    $redirect = 'http://localhost:5070/authentication/login-callback'
    $bytes = New-Object byte[] 32; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $verifier = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::ASCII.GetBytes($verifier))
    $challenge = [Convert]::ToBase64String($hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $query = "client_id=blazor-client&response_type=code&scope=" + [Uri]::EscapeDataString('openid profile email api') +
        "&redirect_uri=" + [Uri]::EscapeDataString($redirect) + "&code_challenge=$challenge&code_challenge_method=S256&state=demo"
    if ($tenant) { $query += "&tenant=$tenant" }
    $url = $Identity + "connect/authorize?$query"
    $code = $null
    for ($i = 0; $i -lt 10 -and -not $code; $i++) {
        $r = Send GET $url -AllowFailure
        if ($r.Status -ge 300 -and $r.Status -lt 400) {
            $next = Absolute $url $r.Location
            if ($next.StartsWith($redirect)) {
                $code = [System.Web.HttpUtility]::ParseQueryString((New-Object Uri($next)).Query)['code']
                if (-not $code) { throw "Sign-in of $email refused: $next" }
                break
            }
            $url = $next; continue
        }
        if ($r.Status -eq 200 -and $r.Text -match 'name="Password"') {
            # The login page: post the form with its antiforgery token and return URL.
            $antiforgery = [regex]::Match($r.Text, 'name="__RequestVerificationToken" type="hidden" value="([^"]+)"').Groups[1].Value
            $returnUrl = [System.Web.HttpUtility]::HtmlDecode([regex]::Match($r.Text, 'name="ReturnUrl" value="([^"]*)"').Groups[1].Value)
            $r = Send POST $url @{ Email = $email; Password = $password; ReturnUrl = $returnUrl; __RequestVerificationToken = $antiforgery } -Form -AllowFailure
            if ($r.Status -lt 300 -or $r.Status -ge 400) { throw "Sign-in of $email failed ($($r.Status))." }
            $url = Absolute $url $r.Location; continue
        }
        throw "Unexpected answer while signing in $email ($($r.Status)) at $url"
    }
    if (-not $code) { throw "No authorization code for $email." }
    $token = Send POST ($Identity + 'connect/token') @{
        grant_type = 'authorization_code'; client_id = 'blazor-client'; code = $code; redirect_uri = $redirect; code_verifier = $verifier
    } -Form
    return $token.Json.access_token
}

function Get-Claims([string] $jwt) {
    $payload = $jwt.Split('.')[1].Replace('-', '+').Replace('_', '/')
    switch ($payload.Length % 4) { 2 { $payload += '==' } 3 { $payload += '=' } }
    return [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
}

function Wait-Until([scriptblock] $probe, [string] $what) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds); $started = Get-Date
    while ((Get-Date) -lt $deadline) {
        $value = & $probe
        if ($value) { return [pscustomobject]@{ Value = $value; Seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1) } }
        Start-Sleep -Seconds 2
    }
    throw "Timed out after $TimeoutSeconds s waiting for $what."
}

# ------------------------------------------------------------------ 0. the playground is up, the accounts sign in

Step 'Playground'
Wait-Until { (Send GET ($Central -replace 'api/organization/$', 'health') -AllowFailure).Status -eq 200 } 'the central service' | Out-Null
Wait-Until { (Send GET ($Gateway + 'health') -AllowFailure).Status -eq 200 } "deployment B's gateway" | Out-Null
Ok 'central (A) and gateway (B) are healthy'

$settings = Join-Path $PlatformRoot 'src\WebApi\Identity\WebApi.Identity.Server\appsettings.Development.json'
if (-not (Test-Path $settings)) { throw "No platform checkout at $PlatformRoot (pass -PlatformRoot)." }
$adminPassword = ((Get-Content $settings -Raw | ConvertFrom-Json).Identity.Seed.Users | Where-Object Email -eq $AdminEmail).Password

$admin = Get-UserToken $AdminEmail $adminPassword $null           # the platform admin: creates orgs on the central service
$manager = Get-UserToken $ManagerEmail $ManagerPassword $Tenant   # the manager in B's tenant
$managerSub = (Get-Claims $manager).sub
$issuer = (Get-Claims $manager).iss
$adminTenant = (Get-Claims $admin).tenant
Ok "signed in: $AdminEmail (platform admin$(if ($adminTenant) { ", tenant $adminTenant" })), $ManagerEmail (tenant $Tenant, sub $managerSub)"

# ------------------------------------------------------------------ 1. org, sector, department, Section

Step 'Org tree on the central service (A)'
$orgKey = 'playground-news'
$org = (Send GET ($Central + 'orgs') -token $admin).Json | Where-Object key -eq $orgKey
if (-not $org) {
    $org = (Send POST ($Central + 'orgs') @{ key = $orgKey; nameEn = 'Playground News'; nameAr = 'أخبار التجربة' } -token $admin).Json
    Ok "created org $orgKey ($($org.id))"
} else { Ok "found org $orgKey ($($org.id))" }
$orgUrl = $Central + "orgs/$($org.id)/"

function Find-OrCreate-Unit($parentId, [string] $type, [string] $nameEn, [string] $nameAr, $categories = $null) {
    $units = @((Send GET ($orgUrl + 'units') -token $admin).Json)
    $unit = $units | Where-Object { $_.parentId -eq $parentId -and $_.typeKey -eq $type } | Select-Object -First 1
    if ($unit) { Ok "found $type '$($unit.nameEn)'"; return $unit }
    $body = @{ parentId = $parentId; typeKey = $type; nameEn = $nameEn; nameAr = $nameAr }
    if ($categories) { $body.categories = $categories }
    $unit = (Send POST ($orgUrl + 'units') $body -token $admin).Json
    Ok "created $type '$nameEn'"
    return $unit
}

$sector = Find-OrCreate-Unit $org.rootUnitId 'sector' 'News Sector' 'قطاع الأخبار'
$department = Find-OrCreate-Unit $sector.id 'department' 'Newsdesk' 'غرفة الأخبار'
# A Section must hold at least one Content post category; the playground has no Content module, so two fixed ids.
$categories = @('7b0f3f6e-0c1a-4a52-9d8e-1f0000000001', '7b0f3f6e-0c1a-4a52-9d8e-1f0000000002')
$section = Find-OrCreate-Unit $department.id 'section' 'Local News' 'الأخبار المحلية' $categories
Check ($section.categories.Count -ge 1) "the Section holds $($section.categories.Count) categories"

# ------------------------------------------------------------------ 2. the manager

Step 'Manager'
$person = @((Send GET ($orgUrl + 'persons') -token $admin).Json) | Where-Object { $_.subject -eq $managerSub } | Select-Object -First 1
if (-not $person) {
    $person = (Send POST ($orgUrl + 'persons') @{ issuer = $issuer; subject = $managerSub; displayName = 'Mona Manager'; email = $ManagerEmail } -token $admin).Json
    Ok "added person Mona Manager"
} else { Ok "found person Mona Manager" }
$position = @((Send GET ($orgUrl + 'positions') -token $admin).Json) | Where-Object key -eq 'manager'
$membership = @((Send GET ($orgUrl + 'memberships') -token $admin).Json) |
    Where-Object { $_.personId -eq $person.id -and $_.unitId -eq $section.id -and $_.positionKey -eq 'manager' } | Select-Object -First 1
if (-not $membership) {
    $membership = (Send POST ($orgUrl + 'memberships') @{ personId = $person.id; unitId = $section.id; positionId = $position.id; primary = $true } -token $admin).Json
    Ok "manager of the Section (position manager: $($position.permissions -join ', '))"
} else { Ok "already manager of the Section" }

# ------------------------------------------------------------------ 3. the seed for B

Step "Tenant seed for deployment '$Deployment'"
$seed = @((Send GET ($orgUrl + 'seeds') -token $admin).Json) | Where-Object tenantId -eq $Tenant | Select-Object -First 1
if (-not $seed) {
    $seed = (Send POST ($orgUrl + 'seeds') @{
        rootUnitId = $department.id; deploymentId = $Deployment; tenantId = $Tenant; name = 'Newsdesk'; hosts = @("$Tenant.localhost")
        databaseServer = 'tenant-data'; databaseName = "tenant-$Tenant"; adminPersonIds = @($person.id)
    } -token $admin).Json
    Ok "created seed $($seed.id) (version $($seed.version)) rooted at the department"
} else { Ok "found seed $($seed.id) (version $($seed.version), $($seed.status))" }

$done = Wait-Until {
    $s = @((Send GET ($orgUrl + 'seeds') -token $admin).Json) | Where-Object id -eq $seed.id
    if ($s.status -in 'Provisioned', 'Adjusted' -and $s.reportedVersion -eq $s.version) { $s }
    elseif ($s.status -in 'Rejected', 'Failed') { throw "Seed $($s.status): $($s.reason)" }
} "B to provision tenant $Tenant"
$seed = $done.Value
Ok "B reported $($seed.status) for version $($seed.version) after $($done.Seconds) s (history: $(($seed.history | ForEach-Object { $_.status }) -join ' > '))"

# ------------------------------------------------------------------ 4. B's replica and grants

Step "B's replica of tenant $Tenant (through B's gateway)"
$local = $Gateway + 'api/organization/local/'
$units = (Wait-Until {
    $r = Send GET ($local + 'units') -token $manager -tenant $Tenant -AllowFailure
    if ($r.Status -eq 200 -and @($r.Json | Where-Object id -eq $section.id).Count -gt 0) { $r.Json }
} "B's replica to hold the Section").Value
$inTenant = @($units | Where-Object inTenant | ForEach-Object id)
$ancestors = @($units | Where-Object { -not $_.inTenant } | ForEach-Object id)
Check ($inTenant -contains $department.id -and $inTenant -contains $section.id) 'the department and the Section are in the tenant'
Check ($ancestors -contains $org.rootUnitId -and $ancestors -contains $sector.id) 'the org and the sector are its ancestors (not in the tenant)'
$cats = @((Send GET ($local + "units/$($section.id)/categories") -token $manager -tenant $Tenant).Json)
Check ($cats.Count -eq $categories.Count) "the Section's categories: $($cats -join ', ')"
$deptCats = @((Send GET ($local + "units/$($department.id)/categories") -token $manager -tenant $Tenant).Json)
Check ($deptCats.Count -eq $categories.Count) "the department's categories are those of its Sections"

$grants = @((Send GET ($Gateway + "api/identity/grants?userId=$managerSub") -token $manager -tenant $Tenant).Json)
$sectionGrants = @($grants | Where-Object { $_.source -eq "org:$($section.id.Replace('-', ''))" })
$deptGrants = @($grants | Where-Object { $_.source -eq "org:$($department.id.Replace('-', ''))" })
$seedGrants = @($grants | Where-Object { $_.source -eq "seed:$($seed.id)" })
Check ($sectionGrants.Count -gt 0) "manager grants on the Section: $(($sectionGrants | ForEach-Object { "$($_.permission)@$($_.scope)" }) -join ', ')"
Check ($deptGrants.Count -gt 0) "seed admin (unit-admin) grants on the department: $(($deptGrants | ForEach-Object { "$($_.permission)@$($_.scope)" }) -join ', ')"
Check ($seedGrants.Count -gt 0) "the host's seed admin grant: $(($seedGrants | ForEach-Object { "$($_.permission)@$($_.scope)" }) -join ', ')"

# The org page follows the live topic organization:org:{orgId:N}, which needs org.read on the org's root unit in the
# subscriber's tenant. No position gives org.read, and a tenant rooted below the org root gets grants on its own units
# only, so nobody holds it there (README, "Troubleshooting"). The manager, as the seed's admin (access.grants.manage),
# grants it to the platform admin, so the admin's org page in this tenant refreshes live.
$adminSub = (Get-Claims $admin).sub
$rootScope = "unit:$($org.rootUnitId.Replace('-', ''))"
$adminGrants = @((Send GET ($Gateway + "api/identity/grants?userId=$adminSub") -token $manager -tenant $Tenant).Json)
if (-not ($adminGrants | Where-Object { $_.permission -eq 'org.read' -and $_.scope -eq $rootScope })) {
    Send POST ($Gateway + 'api/identity/grants') @{ userId = $adminSub; permission = 'org.read'; scope = $rootScope } -token $manager -tenant $Tenant | Out-Null
    Ok "granted $AdminEmail org.read on the org root ($rootScope) in tenant $Tenant (for the live org page)"
} else { Ok "$AdminEmail holds org.read on the org root in tenant $Tenant (for the live org page)" }

# ------------------------------------------------------------------ 5. an edit on central reaches B

Step 'Edit on the central service reaches B'
$current = (Send GET ($orgUrl + "units/$($section.id)") -token $admin).Json
$stamp = (Get-Date).ToString('HH:mm:ss')
$newName = "Local News $stamp"
Send PUT ($orgUrl + "units/$($section.id)") @{
    version = $current.version; nameEn = $newName; nameAr = "الأخبار المحلية $stamp"; tags = $current.tags; timeZone = $current.timeZone; categories = $current.categories
} -token $admin | Out-Null
Ok "renamed the Section to '$newName' on A"
$seen = Wait-Until {
    $u = (Send GET ($local + "units/$($section.id)") -token $manager -tenant $Tenant -AllowFailure).Json
    if ($u.nameEn -eq $newName) { $u }
} "B's replica to show the new name"
Ok "B's replica shows '$newName' after $($seen.Seconds) s"

Write-Host ''
Write-Host "Org $($org.id) | root $($org.rootUnitId) | sector $($sector.id) | department $($department.id) | Section $($section.id) | seed $($seed.id)"
Write-Host "Open http://localhost:5070 as $AdminEmail, tenant $Tenant, then Organization > Playground News: run this script again and watch the tree."
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All checks passed.' -ForegroundColor Green
