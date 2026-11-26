<div dir="rtl">

# Organization: دليل التشغيل

[English](../en/README.md)

Organization حزمة واحدة تعمل بوضعين. نشر واحد يشغل **الخدمة المركزية** (`Organization:Mode=Central`): تحفظ شجرة
وحدات كل مؤسسة عميلة وأشخاصها ومناصبها وبذور المستأجرين والروابط. وكل نشر، بما فيه المركزي، يشغل **الموصل**
(connector): يسحب بذور النشر وتغييرات المؤسسات من الخدمة المركزية إلى نسخة متماثلة (replica) في قاعدة بيانات كل مستأجر.

</div>

```
                 identity service (one issuer)
                   ▲ client credentials        ▲ users sign in
                   │                           │
Deployment A (central)                         Deployment B (a connector)
  core pool: Organization, Mode=Central  ◄──── gateway: /api/organization/*        → A (management API)
    organization database (platform level)       /api/organization/local/*  → B's core pool
    connector → its own tenants                  everything else            → B's core pool
                                          ◄──── B's connector: seeds, feed, status reports
                                                 B's seed reconciler creates the tenants, B's replicas serve them
```

<div dir="rtl">

| الصفحة | المحتوى |
| --- | --- |
| [الإعداد](setup.md) | النشر المركزي، ونشر موصل، وعميل الهوية، والبوابة، ومضيف Blazor |
| [مرجع الإعدادات](settings.md) | كل إعداد، وأين يوضع، وقيمته الافتراضية |
| [بذور المستأجرين](seeds.md) | كيف تحصل وحدة على مستأجر خاص بها في نشر ما، والحالة التي يبلغ عنها |
| [روابط المستأجرين](links.md) | الهيكل الأولي للروابط بين المستأجرين الأشقاء |
| [حل المشكلات](troubleshooting.md) | الأعراض وأسبابها وحلولها، ومنها ثغرتان معروفتان في هذا الإصدار التجريبي |

يشرح [README](../../README.md) في المستودع الواجهة البرمجية والبناء وبيئة التجربة (playground) وخطوات الإصدار.

</div>
