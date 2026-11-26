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
       sector as ancestors, the Section's categories, and the managers' grants (source org:{unitId}), among them
       org.read on the department (the tenant's seed root), and subscribes to the org's live topic on B's hub
       (/hubs/live) as the manager, the subscription the org page's Live chip makes;
    5. renames the Section on the central service, waits until B's replica has it and the live topic has nudged the
       manager.

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

# ------------------------------------------------------------------ the live hub (SignalR JSON protocol over a WebSocket)

$RS = [char]0x1e   # the protocol's record separator

function Send-Live($live, [string] $json) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($json + $RS)
    $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList (,$bytes)
    [void] $live.Socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
}

# The next record that is not a ping, or $null after $seconds (a timeout aborts the socket).
function Receive-Live($live, [int] $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ($true) {
        $i = $live.Buffer.IndexOf($RS)
        if ($i -ge 0) {
            $text = $live.Buffer.Substring(0, $i); $live.Buffer = $live.Buffer.Substring($i + 1)
            $record = $text | ConvertFrom-Json
            if ($record.type -ne 6) { return $record } else { continue }
        }
        $left = $deadline - (Get-Date)
        if ($left.TotalMilliseconds -le 0) { return $null }
        $cts = New-Object Threading.CancellationTokenSource($left)
        $buffer = New-Object byte[] 16384
        $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList (,$buffer)
        try { $r = $live.Socket.ReceiveAsync($segment, $cts.Token).GetAwaiter().GetResult() } catch { return $null }
        if ($r.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) { throw "The live hub closed the connection: $($r.CloseStatusDescription)" }
        $live.Buffer += [Text.Encoding]::UTF8.GetString($buffer, 0, $r.Count)
    }
}

function Open-Live([string] $token, [string] $tenant) {
    $socket = New-Object System.Net.WebSockets.ClientWebSocket
    $socket.Options.SetRequestHeader('Authorization', "Bearer $token")
    if ($tenant) { $socket.Options.SetRequestHeader('X-Tenant', $tenant) }
    [void] $socket.ConnectAsync([Uri](($Gateway -replace '^http', 'ws') + 'hubs/live'), [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $live = @{ Socket = $socket; Buffer = ''; Next = 0 }
    Send-Live $live '{"protocol":"json","version":1}'
    $handshake = Receive-Live $live 10
    if ($null -eq $handshake -or $handshake.error) { throw "The live hub refused the handshake: $($handshake.error)" }
    return $live
}

# Subscribe(topic) on the hub: the LiveSubscribeResult (ok, error).
function Subscribe-Live($live, [string] $topic) {
    $live.Next = $live.Next + 1
    $id = [string] $live.Next
    Send-Live $live (@{ type = 1; invocationId = $id; target = 'Subscribe'; arguments = @($topic) } | ConvertTo-Json -Compress)
    while ($true) {
        $record = Receive-Live $live 15
        if ($null -eq $record) { throw "No answer from the live hub to Subscribe($topic)." }
        if ($record.type -eq 3 -and $record.invocationId -eq $id) {
            if ($record.error) { throw "Subscribe($topic) failed: $($record.error)" }
            return $record.result
        }
    }
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

# The org page's Live chip is its subscription to the live topic organization:org:{orgId:N}. On a connector tenant the
# topic's scopes are the org root plus the tenant's seed root and its ancestors, and every position gives org.read on
# its unit, so the seed's admin (unit-admin on the department) may subscribe with no hand-made grant.
$deptScope = "unit:$($department.id.Replace('-', ''))"
Check (@($deptGrants | Where-Object { $_.permission -eq 'org.read' -and $_.scope -eq $deptScope }).Count -eq 1) "the manager holds org.read on the seed root ($deptScope) from the unit-admin position"
$orgTopic = "organization:org:$($org.id.Replace('-', ''))"
$live = Open-Live $manager $Tenant
$subscribed = Subscribe-Live $live $orgTopic
Check ([bool] $subscribed.ok) "the manager subscribed to $orgTopic on B's live hub$(if (-not $subscribed.ok) { ": $($subscribed.error)" }) (the org page's Live chip)"

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
if ($subscribed.ok) {
    $nudge = $null
    while ($null -eq $nudge) {
        $record = Receive-Live $live 30
        if ($null -eq $record) { break }
        if ($record.type -eq 1 -and $record.target -eq 'Message') { $nudge = $record }
    }
    Check ($null -ne $nudge) "the live topic nudged the manager ($(if ($nudge) { ($nudge.arguments | ConvertTo-Json -Compress -Depth 5) -replace '^(.{160}).+$', '$1...' } else { 'nothing within 30 s' }))"
}
if ($live.Socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
    [void] $live.Socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'done', [Threading.CancellationToken]::None).GetAwaiter().GetResult()
}

Write-Host ''
Write-Host "Org $($org.id) | root $($org.rootUnitId) | sector $($sector.id) | department $($department.id) | Section $($section.id) | seed $($seed.id)"
Write-Host "Open http://localhost:5070 as $ManagerEmail (password $ManagerPassword), then Organization > Playground News: the tree shows Live; run this script again and watch it."
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All checks passed.' -ForegroundColor Green
