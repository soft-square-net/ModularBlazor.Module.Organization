<div dir="rtl">

# الإعداد: المركزي والموصل

[English](../en/setup.md) · [مرجع الإعدادات](settings.md)

تُثبت الحزمة نفسها (`organization-<version>.zip`) على كل نشر، ويحدد إعداد واحد ما تفعله:

| النشر | `Organization:Mode` | `Organization:CentralUrl` | ما يعمل |
| --- | --- | --- | --- |
| النشر المركزي (واحد فقط) | `Central` | مسار الوحدة فيه هو نفسه | الواجهة البرمجية للإدارة وقاعدة بياناتها، وموصله لمستأجريه |
| كل نشر آخر | `Connector` (الافتراضي) | مسار وحدة الخدمة المركزية | الموصل وحده؛ وتجيب الواجهة البرمجية للإدارة بـ 404 |

ثبتها كأي وحدة: ارفع الجزء الخادمي في صفحة Host Modules (`/host-modules`) في كل نشر وفعّله، ثم أجزاء العميل في صفحة
Client Modules (`/client-modules`)، عميل الواجهة البرمجية أولا. حمّلها على **المجمع الأساسي (core pool)** في كل نشر:
فمركز التحديثات المباشرة (live hub) يعمل هناك ويسأل الوحدة عن نطاقات الموضوع.

## 1. سجّل عميل خدمة الموصل

يستدعي كل موصل الخدمة المركزية برمز client credentials. سجّل عميلا سريا (confidential) واحدا في خدمة الهوية (صفحة
**Applications** في إدارة الهوية): نوع المنح client credentials، والنطاق `api` (جمهور WebApi في المنصة،
`modular-webapi`). دوّن معرف العميل (مثلا `organization-connector`) واحفظ السر في مخزن الأسرار. يكفي عميل واحد لكل
النشرات، وعميل لكل نشر يتيح لك إلغاء واحد منها وحده.

قيمة `sub` في الرمز هي معرف العميل، ولا تقبله الخدمة المركزية إلا إن كان مدرجا في `Organization:ServiceClients`.

## 2. النشر المركزي

إعدادات الوحدة (صفحة Host Modules، أو تجاوزات المضيف `Modules:Settings:organization:Organization:<Key>`):

</div>

```
Organization:Mode              = Central
Organization:TrustedIssuers    = https://id.example/              (the identity service's issuer; empty: any issuer the host trusts)
Organization:ServiceClients    = organization-connector           (comma-separated client ids)
Organization:DatabaseServer    = TenantStore                      (a server the host knows; default)
Organization:DatabaseName      = organization                     (created on first start; default)
```

<div dir="rtl">

والموصل موجّها إلى النشر نفسه (لمستأجريه نسخ متماثلة مثل غيرهم):

</div>

```
Organization:CentralUrl        = https://central.example/api/organization/
Organization:TokenEndpoint     = https://id.example/connect/token
Organization:ClientId          = organization-connector
Organization:ClientScope       = api
```

<div dir="rtl">

إعدادات المضيف (متغيرات البيئة أو مخزن الأسرار)، ولا توضع أبدا في كتالوج الوحدات:

</div>

```
Modules__Settings__organization__Organization__ClientSecret = <the client's secret>
Platform__DeploymentId                                      = central
```

<div dir="rtl">

عند البدء تنشئ الخدمة المركزية قاعدة البيانات `organization` (المخطط `organization`) وتطبق ترحيلاتها، وتعيد المحاولة
كل 30 ثانية ما دام الخادم غير متاح. والواجهة البرمجية للإدارة تحت `/api/organization`.

## 3. نشر موصل

إعدادات الوحدة:

</div>

```
Organization:Mode              = Connector                        (default)
Organization:CentralUrl        = https://central.example/api/organization/
Organization:TokenEndpoint     = https://id.example/connect/token
Organization:ClientId          = organization-connector
Organization:ClientScope       = api
```

<div dir="rtl">

إعدادات المضيف:

</div>

```
Modules__Settings__organization__Organization__ClientSecret = <the client's secret>
Platform__DeploymentId                                      = branch      (this deployment's id, unique)
```

<div dir="rtl">

يشغّل `Platform:DeploymentId` مسوّي البذور (seed reconciler) في المضيف: يسحب بذور هذا المعرف عبر الموصل، وينشئ
المستأجرين، ويبلغ حالتهم. ويستخدم الموصل المعرف نفسه (أو `Organization:DeploymentId` إن ضُبط). ومن دونه لا يُنشأ شيء.

## 4. بوابة نشر موصل

يستدعي عميل نشر الموصل الواجهة البرمجية للإدارة (المؤسسات والوحدات والأشخاص والبذور) عبر بوابته هو، فترسلها البوابة إلى
الخدمة المركزية. أما المسارات المحلية للوحدة، التي تقرأ النسخة المتماثلة في هذا النشر، وكل ما عداها، فتبقى في النشر.
مع YARP (الترتيب مهم: المسار الأدق أولا):

</div>

```csharp
yarp.AddRoute("/api/organization/local/{**catch-all}", coreCluster);      // this deployment's replica
yarp.AddRoute("/api/organization/{**catch-all}", centralCluster);         // the central service
yarp.AddRoute("/{**catch-all}", coreCluster);                             // everything else, the live hub included
```

<div dir="rtl">

`centralCluster` هو العنوان العام للنشر المركزي. أما النشر المركزي نفسه فلا يحتاج مسارا إضافيا: مساره العام
(catch-all) يرسل `/api/organization` إلى مجمعه الأساسي أصلا.

بدلا من المسار، يمكن لمضيف Blazor أن يستدعي الخدمة المركزية مباشرة: اضبط `Organization:BaseUrl` في
`wwwroot/appsettings.json` (الفارغ يعني البوابة). وعندها يجب أن تسمح الخدمة المركزية بأصل العميل (CORS).

### مستخدمو النشرات الأخرى

يستدعي مستخدمو نشر الموصل الخدمة المركزية برمز لأحد مستأجري نشرهم *هم*، ولا يعرفه النشر المركزي. وابتداء من المنصة
1.0.0-preview.8 تقبله مسارات الوحدة المركزية، وتفوّض الخدمة هذا المستخدم *الأجنبي* من بياناتها هي. ولا يحتاج النشر
المركزي إلى مستأجر بالمعرف نفسه.

- يجب أن تكون مطالبة `tenant` في الرمز هي ذلك المستأجر (يُرفض `X-Tenant` وحده).
- لا يصل المستخدم إلا إلى المؤسسات التي لها [بذرة مستأجر](seeds.md) بمعرف ذلك المستأجر؛ وداخلها يقرر كونه مسؤولا عن
  المؤسسة أو شخصا فيها أو عضوا بمنصب (بجهة إصدار الرمز و`sub`).
- لا يسري دور `platform-admin`. يدير مسؤولو المنصة الخدمة المركزية بتسجيل الدخول بلا مستأجر، أو في مستأجر يعرفه النشر
  المركزي.

تُطابَق معرفات المستأجرين بالاسم بين النشرات، ويجب أن تكون خدمة الهوية التي أصدرت الرمز موثوقة
(`Organization:TrustedIssuers`). ومعرف المستأجر الموجود في النشر المركزي أيضا هو ذلك المستأجر المركزي، فلا تعد استخدامه
لمستأجر آخر.

## 5. تحقق

1. أنشئ مؤسسة بصفتك مسؤول منصة (`POST /api/organization/orgs` على الخدمة المركزية، أو صفحات **Organization**).
2. ابن الشجرة، وأضف شخصا وعضوية بمنصب.
3. أنشئ [بذرة مستأجر](seeds.md) لمعرف نشر الموصل. خلال دورة واحدة للمسوي (`Tenancy:Seeds:Interval`، دقيقة افتراضيا،
   أو فورا بعد السحب التالي للموصل) تنتقل حالة البذرة: Requested ثم Provisioning ثم Provisioned.
4. في المستأجر الجديد، يعرض `GET /api/organization/local/units` وحدة جذر البذرة والوحدات تحتها، وتظهر مناصب الأعضاء
   منحا (`GET /api/identity/grants?source=org:{unitId:N}`).

تفعل بيئة التجربة في المستودع كل هذا على نشرين محليين: `playground/demo.ps1` (انظر README، قسم "Developing").

</div>
