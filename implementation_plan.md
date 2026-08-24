# NovaCMS Uygulama Planı

## 1. Belgenin Amacı ve Durumu

Bu belge; NovaCMS için mimari referans, uygulama yol haritası, kapsam sözleşmesi ve mühendislik karar rehberidir. Profesyonel MVP'yi ve production-ready `v1.0.0` sürümüne uzanan yolu tanımlar; açıklanan sistemin hâlihazırda uygulanmış olduğu anlamına gelmez.

NovaCMS, teknik bilgisi sınırlı kullanıcıların modern, responsive ve SEO uyumlu bir web sitesi oluşturup yönetebilmesini sağlayan modüler bir içerik yönetim sistemi ve section-based website builder olacaktır. v1 için temel kurulum modeli şudur:

> **Bir NovaCMS kurulumu, bir web sitesine karşılık gelir.**

Amaç; WordPress, Wix veya Webflow ile özellik eşitliği değil, odaklı ve sürdürülebilir bir ürün geliştirmektir. Kararlar; açık sınırlar, güvenlik, test edilebilirlik, operasyonel sadelik ve kanıtlanabilir production disiplini doğrultusunda alınmalıdır.

### 1.1 Kapsam terminolojisi

- **MVP**, temel içerik oluşturma ve yayınlama akışını doğrulamak için gereken en küçük, tutarlı uygulamadır.
- **v1**, Faz 0–9 sonunda ortaya çıkan production-ready sonuçtur. Bir yetenek v1 kapsamında bulunabilir ancak ilk MVP artımı için zorunlu olmayabilir.
- **Gelecek yol haritası**, açıkça ertelenmiş fikirleri içerir. Bu maddeler bilinçli bir kapsam kararı olmadan v1'e dâhil edilmemelidir.
- Bu planda belirtilen timeout, token ömrü, yükleme limiti ve benzeri değerler başlangıç rehberidir ve yapılandırılabilir kalmalıdır.

## 2. Ürün Vizyonu ve Başarı Ölçütleri

NovaCMS; bir yöneticinin kimlik doğrulaması yapmasını, siteyi yapılandırmasını, kontrollü section varyantlarından sayfa oluşturmasını, medyayı yönetmesini, yayınlanmamış çalışmayı önizlemesini ve responsive bir public web sitesi yayınlamasını sağlamalıdır. Blog ve iletişim yetenekleri ilk içerik platformunu tamamlar.

v1 aşağıdaki koşullarda başarılı kabul edilir:

- teknik bilgisi olmayan bir kullanıcı, kritik içerik oluşturma akışını kod düzenlemeden tamamlayabilir;
- taslak içerik public endpoint'lerden erişilemez;
- public renderer, saklanan yapılandırmayı güvenli biçimde bilinen frontend component'lerine eşleyebilir;
- medya, kimlik doğrulama ve kullanıcı kaynaklı içerik uygun güvenlik kontrolleriyle işlenir;
- sistem tekrarlanabilir biçimde build, test, deploy ve observe edilebilir; yeterli biçimde dokümante edilmiştir;
- mimari sınırlar, erken dağıtık sistem karmaşıklığı oluşturmadan gelecekteki genişlemelere izin verecek kadar açıktır.

## 3. Kapsam Sözleşmesi

### 3.1 v1 yetenekleri

v1 kapsamı; admin kimlik doğrulaması, site ve tema ayarları, sayfalar, section-based kompozisyon, kontrollü Component Registry, medya yönetimi, navigasyon, public renderer, temel SEO, blog içeriği, iletişim mesajları, güvenlik kontrolleri, testler, CI/CD ve production deployment temellerini içerir.

### 3.2 Kısıtlar

- Backend, microservice koleksiyonu değil Modular Monolith olacaktır.
- Admin ve public deneyimleri Next.js ve TypeScript kullanır; ancak güven sınırları ve kullanım amaçları farklıdır.
- PostgreSQL system of record olacaktır. JSONB, ilişkisel modellemenin yerine geçmek için değil, değişken section yapılandırmalarında seçici biçimde kullanılacaktır.
- Çalıştırılabilir frontend kodu veritabanında tutulmayacaktır.
- Infrastructure sağlayıcılarına Application katmanına dönük abstraction'lar üzerinden erişilecektir.
- Redis, message broker, Kubernetes ve benzeri infrastructure bileşenleri ancak ölçülmüş ihtiyaçla gerekçelendirilecektir.

## 4. Sistem Mimarisi

### 4.1 Teknoloji temeli

| Alan | Teknoloji | Sorumluluk |
|---|---|---|
| Admin paneli | Next.js, TypeScript, Tailwind CSS | Kimliği doğrulanmış içerik ve yapılandırma yönetimi |
| Public web sitesi | Next.js, TypeScript | Responsive public rendering, SEO çıktısı, preview yönetimi |
| Backend | .NET 10 üzerinde ASP.NET Core Web API | İş use case'leri, authorization, validation ve persistence orchestration |
| Veritabanı | EF Core ile PostgreSQL | Transactional veri, constraint'ler, index'ler ve seçici JSONB saklama |
| Mimari | Modular Monolith | Tek operasyonel backend biriminde güçlü modül sınırları |

Kesin framework/package sürümleri, repository yerleşim ayrıntıları ve deployment sağlayıcıları uygulama sırasında seçildiğinde kayda geçirilmelidir. Önceden kararlaştırılmış gibi gösterilmemelidir.

### 4.2 Backend katmanları

Planlanan solution sınırları:

| Proje | Sorumluluk | Sahip olmaması gerekenler |
|---|---|---|
| `NovaCMS.Api` | HTTP endpoint'leri, authentication altyapısı, request/response contract'ları, middleware ve API composition | Domain kuralları veya sağlayıcıya özel persistence mantığı |
| `NovaCMS.Application` | Use case'ler, command/query'ler, orchestration, validation sınırları, `IFileStorage` ve `IEmailSender` gibi port'lar | PostgreSQL, S3, SMTP veya web framework implementation ayrıntıları |
| `NovaCMS.Domain` | Temel entity'ler, value object'ler, invariant'lar, iş kuralları ve gerekçeliyse domain event'leri | EF Core, HTTP, storage, e-posta veya vendor bağımlılıkları |
| `NovaCMS.Infrastructure` | EF Core, PostgreSQL mapping'leri, repository implementation'ları, file storage, e-posta ve diğer adapter'lar | Presentation concern'leri veya iş politikası sahipliği |

Dependency yönü iç katmanları korumalıdır: Domain ve Application; PostgreSQL, S3-compatible storage, SMTP veya başka Infrastructure implementation'larına doğrudan bağımlı olmamalıdır. Interface'ler yeteneği tüketen sınırda bulunur; Infrastructure değiştirilebilir adapter'ları sağlar.

Modüller başlangıçta aynı veritabanını ve process'i paylaşabilir; ancak sorumlulukları ve API'leri açık kalmalıdır. Modular Monolith, sürdürülebilir sınırları korurken deployment ve tutarlılık karmaşıklığını azaltır.

### 4.3 Üst düzey akışlar

```text
Admin Panel
  -> /api/v1/admin/...
  -> ASP.NET Core API
  -> Application / Domain
  -> Infrastructure
  -> PostgreSQL

Public Website
  -> /api/v1/public/...
  -> Published Page + Visible Sections
  -> Component Registry
  -> Known Next.js Components
  -> Rendered Website
```

Admin ve public endpoint'ler; authentication, authorization, response şekli, Cache ve dışa açılma riski bakımından farklı oldukları için ayrı route group'ları kullanır. Bu ayrım ayrı microservice gerektirmez.

## 5. Temel Domain ve Fonksiyonel Tasarım

### 5.1 Sayfa yönetimi

Bir Page en az `Id`, `Title`, normalize edilmiş benzersiz `Slug`, `Status`, SEO metadata, `CreatedAt`, `UpdatedAt`, `CreatedBy` ve `UpdatedBy` alanlarını gerektirir. Soft Delete metadata yalnızca geri alma, audit veya referans bütünlüğü gerektiriyorsa eklenebilir.

İlk status değerleri `Draft` ve `Published` olacaktır. Desteklenen use case'ler: oluşturma, güncelleme, silme, çoğaltma, taslak kaydetme, yayınlama, preview, slug yönetimi, SEO yönetimi ve pagination uygulanmış listelemedir.

Slug politikası merkezi ve test edilmiş olmalıdır:

- büyük/küçük harf, whitespace, ayraç ve desteklenen karakterleri tutarlı şekilde normalize etmek;
- benzersizliği hem Application validation hem veritabanı unique constraint ile uygulamak;
- boş veya geçersiz sonuçları reddetmek;
- `admin`, `api` ve `login` gibi uygulama route'larını, normalize eşdeğerleriyle birlikte rezerve etmek;
- published bir slug değişikliğinin mevcut URL'lere etkisini implementation öncesi tanımlamak; advanced redirect manager v1 kapsamında değildir.

Yayınlama açık bir iş durumu geçişidir. Public query'ler yalnızca published, silinmemiş sayfaları ve görünür section'ları seçmelidir. Client'ın gönderdiği status veya hidden alanı hiçbir zaman tek enforcement mekanizması olmamalıdır.

### 5.2 Section Tabanlı Sayfa Oluşturucu

v1 builder, free-form pixel editor yerine bilinçli olarak section-based tasarlanır:

```text
Page -> ordered PageSection[]
```

PageSection; `Id`, `PageId`, `ComponentKey`, JSONB `Settings`, `SortOrder` ve `IsVisible` içerir. API; ekleme, düzenleme, çoğaltma, gizleme/gösterme, silme ve sıralama işlemlerini destekler.

JSONB, component türüne göre değişen ayarlar için uygundur:

```json
{
  "title": "Build better products",
  "description": "Modern digital solutions.",
  "backgroundImageId": "media-id",
  "primaryButton": {
    "text": "Contact",
    "url": "/contact"
  }
}
```

Her `ComponentKey`, bilinen bir yapılandırma şemasına ve server-side validation'a sahip olmalıdır. JSONB kullanımı; input validation, authorization, migration stratejisi veya media ID referanslarının kullanım kontrollerini ortadan kaldırmaz.

Reorder işlemi; section identifier'larını ve hedef konumları alan, tüm section'ların aynı sayfaya ait olduğunu doğrulayan ve yalnızca sıralama verisini tek transaction içinde güncelleyen özel bir operasyon kullanmalıdır. Client'ın section içeriğini yeniden göndermesi veya üzerine yazması gerekmemelidir. Başlangıçta basit integer sıralama kabul edilebilir; fractional ranking ancak ölçülen contention veya ölçek ihtiyacıyla gerekçelendirilmelidir.

### 5.3 Component Registry

Public Next.js uygulaması aşağıdaki gibi kontrollü bir registry'nin sahibidir:

```text
hero-modern      -> HeroModern
hero-corporate   -> HeroCorporate
services-grid    -> ServicesGrid
```

Veritabanı, onaylı hangi component'in ve doğrulanmış hangi içeriğin gösterileceğini belirler. Frontend kodu nasıl render edileceğini belirler. Çalıştırılabilir JavaScript, arbitrary import veya component source hiçbir zaman veritabanından saklanmaz ya da evaluate edilmez.

Bu sınır; remote-code execution ve kontrolsüz markup davranışını önler, code review ile automated test'i destekler, component değişikliklerini versionable yapar ve bilinmeyen key'ler için öngörülebilir fallback davranışı sağlar. Registry girdileri gelecekte configuration metadata veya schema sunarak admin tarafında schema-driven editor üretimini sağlamalı; API de aynı contract'ı doğrulamalıdır. Production'da saklanan settings değiştirilmeden önce schema/version evrimi planlanmalıdır.

### 5.4 İçerik Oluşturma ve Yayınlama Akışı

```text
Login -> Dashboard -> Pages -> Create Page -> Page Settings
      -> Add Section -> Select Component Variant -> Edit Section
      -> Reorder Sections -> Preview -> Save Draft -> Publish
```

Kaydetme ve yayınlama ayrı işlemlerdir. Preview; authenticated, authorized ve kısa ömürlü bir mekanizma kullanmalı; draft veriyi cache edilebilir veya public olarak enumerate edilebilir hâle getirmemelidir. Public kullanıcılar public API veya renderer fallback'leri üzerinden draft sayfalara ulaşamamalıdır. Publish işlemi aggregate'in tamamını doğrulamalı, gereken geçişi atomik olarak persist etmeli ve ilgili public Cache'i yalnızca başarıdan sonra invalidate etmelidir.

### 5.5 Medya Kütüphanesi

Medya ayrı bir modüldür. `MediaAsset`; `Id`, `OriginalFileName`, `StoredFileName`, `StorageKey`, resolved location veya `Url`, `MimeType`, `Extension`, `Size`, isteğe bağlı `Width` ve `Height`, `AltText`, `Title`, `CreatedAt` ve `UpdatedAt` alanlarını temsil etmelidir.

Section settings mümkün olduğunca kalıcı storage URL yerine `MediaAssetId` saklamalıdır. ID'ler içeriği provider, bucket, domain, CDN ve URL-signing değişikliklerinden ayırır; metadata güncellemeyi, authorization'ı, kullanım takibini, dönüşümleri ve güvenli silmeyi mümkün kılar. URL'ler delivery sınırında resolve edilmelidir.

Application `IFileStorage` tanımlar; Infrastructure başlangıçta şunları sağlar:

- development için `LocalFileStorage`;
- production için S3-compatible implementation.

Provider ayrıntıları Domain'e sızmamalıdır. Upload işlemi; request size limit, izin verilen extension listesi, MIME validation, file signature/magic byte validation, random storage key, path traversal koruması, yapılandırılabilir limitler ve güvenli gösterim/saklama adları gerektirir. Kullanıcının sağladığı filename metadata'dır, güvenilir path değildir.

MVP medya kapsamı upload, selection, metadata, alt text, usage tracking/protection ve safe deletion içerir. Referans verilen bir asset'in silinmesi, bilinçli replacement/detachment akışı tamamlanmadıkça açıklayıcı bir conflict ile engellenmelidir. Fiziksel object silme ve veritabanı mutation işlemleri, kısmi başarısızlıkların sessiz orphan state oluşturmaması için failure handling içermelidir.

WebP conversion, image resizing, responsive variants ve thumbnail pipeline ertelenmiştir.

### 5.6 Tema sistemi

Global Theme Settings; `PrimaryColor`, `SecondaryColor`, `BackgroundColor`, `TextColor`, `HeadingFont`, `BodyFont`, `BorderRadius`, `ContainerWidth` ve `ButtonStyle` içerir. Public uygulama doğrulanmış değerleri CSS variables veya başka bir merkezi theme mekanizmasıyla sunmalıdır.

Section'lar global design token'ları tekrar etmek yerine inherit etmelidir. Section-level override'lar amaçlı ve schema-defined seçeneklerle sınırlanmalıdır. Renkler, boyutlar, font seçimleri ve CSS'e aktarılan değerler kullanımdan önce allowlist veya güvenli normalization kontrolünden geçmelidir.

### 5.7 Site ayarları

Site Settings; `SiteName`, `SiteDescription`, `LogoMediaId`, `FaviconMediaId`, `DefaultSeoTitle`, `DefaultSeoDescription`, `ContactEmail`, `ContactPhone`, `Address` ve doğrulanmış `SocialLinks` içerir.

SEO metadata açık bir fallback zinciri izler: page veya post override, ardından site default ve gerektiğinde deterministic title/description fallback. Media referansları Media modülünü ve bu modülün deletion protection kurallarını kullanır.

### 5.8 Menü yönetimi

NovaCMS header ve footer menülerini destekler. Bir `MenuItem`; `Label`, `URL`, isteğe bağlı `PageId`, `SortOrder`, isteğe bağlı `ParentId`, `OpenInNewTab` ve `IsVisible` gerektirir.

MVP; internal page link'leri, manuel external link'ler, nested menu ve header/footer yerleşimini destekler. Validation, geçersiz protocol'leri ve cyclic parent ilişkilerini önlemelidir. Internal `PageId` link'leri stale URL kopyalamak yerine güncel slug'ı resolve etmelidir. Mega menu ertelenmiştir.

### 5.9 Kimlik Doğrulama ve Yetkilendirme

İlk ürün single-admin ağırlıklı olabilir; ancak ağır bir permission engine uygulamadan, model gelecekte `Admin`, `Editor` ve `Author` rollerini destekleyebilmelidir.

User; `Id`, normalize edilmiş benzersiz `Email`, `PasswordHash`, `FirstName`, `LastName`, `IsActive`, `CreatedAt`, `UpdatedAt` ve `LastLoginAt` içerir. Password'ler güncel ve adaptive bir password-hashing mekanizmasıyla, bakımı yapılan framework olanakları üzerinden korunmalıdır; reversible encryption veya custom cryptography kullanılmamalıdır.

Authentication şunları içerir:

- kısa ömürlü access token ve Refresh Token;
- Refresh Token rotation ve replay-aware revocation;
- logout ve logout-all;
- login Rate Limiting ve generic invalid-login response;
- ortama özel strict CORS;
- repository dışında tutulan secret'lar.

Başlangıç rehberi access token için yaklaşık 15 dakika, Refresh Token için yaklaşık 7 gün olabilir; ancak ikisi de hard-coded business rule değil, yapılandırılabilir security setting olmalıdır.

RefreshToken modeli; `Id`, `UserId`, `TokenHash`, `ExpiresAt`, `CreatedAt`, `RevokedAt` ve `ReplacedByTokenId` içerir. Yalnızca token'ın cryptographic hash'i persist edilir. Rotation, sunulan token'ı revoke etmeli ve replacement bağlantısını atomik olarak kurmalıdır; reuse tespitinde dokümante edilen politika uyarınca ilgili token family veya session revoke edilmelidir.

Browser client'ları için HttpOnly, Secure ve uygun SameSite cookie'leri Refresh Token yönetiminde tercih edilen temeldir. Nihai access/refresh saklama ve taşıma tasarımı, implementation öncesinde deployment topology ve açık bir CSRF/XSS threat model ile doğrulanmalıdır. Cookie-based state-changing request'ler CSRF koruması gerektirir; cross-origin varsayımlar test edilmelidir.

### 5.10 SEO

Page ve BlogPost; `SeoTitle`, `SeoDescription`, `CanonicalUrl`, `NoIndex`, `NoFollow`, `OpenGraphTitle`, `OpenGraphDescription` ve `OpenGraphImageMediaId` destekleyebilir.

Public uygulama doğru metadata, `sitemap.xml` ve `robots.txt` üretir. Sitemap yalnızca uygun published canonical content içerir. Canonical URL'ler malformed veya hostile çıktı oluşturmamak için doğrulanmalıdır. SEO score, broken-link checker ve advanced redirect manager v1 sonrası adaylardır.

### 5.11 Blog

Blog, Page Builder'ın özel bir biçimi değil ayrı bir içerik modülüdür. `BlogPost`; `Id`, `Title`, benzersiz normalize edilmiş `Slug`, `Excerpt`, `Content`, `FeaturedImageId`, `Status`, `PublishedAt`, `AuthorId`, SEO information, `CreatedAt` ve `UpdatedAt` içerir. Category ve Tag modelleri düzenleme sağlar.

MVP rich text editor kullanabilir; ancak saklanan içerik dokümante edilmiş bir sanitization politikası izlemelidir. Raw executable JavaScript veya kontrolsüz HTML çalıştırılmamalıdır. Public listeler paginated, deterministic ve yayın zamanı uygun published post'larla sınırlı olmalıdır.

### 5.12 İletişim formu

Public akış:

```text
Request -> validation -> rate limiting -> spam controls -> database
```

`ContactMessage`; `Id`, `Name`, `Email`, isteğe bağlı `Phone`, isteğe bağlı `Subject`, `Message`, status (örneğin Unread, Read, Archived) ve `CreatedAt` içerir. Admin rendering, gönderilen tüm alanları untrusted text olarak ele almalı ve output'u güvenli biçimde encode etmelidir.

E-posta bildirimi, Application katmanındaki `IEmailSender` arkasında isteğe bağlı bir side effect'tir; Infrastructure, `SmtpEmailSender` gibi bir adapter sağlayabilir. Notification delivery başarısızlığı persistence güvenilirliğini bozmamalıdır; retry/operational davranış, MVP için message broker zorunlu kılmadan bilinçli biçimde tanımlanmalıdır.

## 6. Güvenlik Mimarisi

Güvenlik, sonradan yapılan bir hardening aşaması değil feature tasarımı ve kabul kriterinin parçasıdır. Kontroller threat model ile bağlantısız süreç yükü oluşturmadan gerçek risklere odaklanmalıdır.

| Alan | Temel karar |
|---|---|
| Authentication | Bakımı yapılan password hashing, kısa access lifetime, hash'lenmiş rotating Refresh Token, revocation, inactive-user kontrolü |
| Authorization | Her admin use case ve protected preview için server-side kontrol; default deny |
| Input validation | API sınırlarında merkezi ve tutarlı validation ile Domain invariant'ları |
| Output handling | Context-aware encoding; izin verilen rich text'i sanitize etmek; arbitrary script render etmemek |
| File upload | Size, extension, MIME ve signature kontrolü; random key; güvenli ad; kullanıcı path'ine güvenmemek |
| XSS | Kontrollü Component Registry, React'in güvenli rendering default'ları, HTML sanitization, restrictive content policy |
| CSRF | Cookie akışlarını threat-model etmek; state-changing cookie-authenticated request'lerde anti-forgery koruması |
| CORS | Açıkça tanımlı production origin, method ve header'lar; credential ile permissive wildcard kullanmamak |
| Abuse | Uygun login, refresh, preview-sensitive path, upload ve public form Rate Limiting |
| Secrets | Environment veya secret manager; source control, client bundle veya log içinde bulundurmamak |
| Cookies | Production'da `HttpOnly`, `Secure`, uygun `SameSite`, dar path/domain ve lifetime |
| Database | Varsayılan EF Core parameterization; raw SQL review; least-privilege database credential |
| Logging | Password, token, cookie, connection string, API secret ve hassas mesaj verisini filtrelemek |
| Headers | Uygun yerde HTTPS redirection/HSTS, gerçek asset'lere göre CSP, anti-sniffing ve framing policy |
| Transport | Production'da HTTPS zorunluluğu; açık trusted-proxy/forwarded-header yapılandırması |

Tekrarlanan login failure ve Refresh Token reuse gibi security-sensitive event'ler credential veya kişisel veri açığa çıkarmadan observable olmalıdır. Dependency'ler desteklenen sürümlerde tutulmalı ve CI'da gözden geçirilmelidir. Error message'lar operator için yararlı olurken account enumeration ve internal detail leakage oluşturmamalıdır.

## 7. Veri Bütünlüğü ve Kalıcı Veri Yönetimi

EF Core migration'ları review edilen artifact'lardır ve kontrollü environment'lar üzerinden ileri taşınır; production schema ad hoc değiştirilmez. Application önce validation yapsa bile relational constraint'ler nihai integrity boundary'dir.

Gerekli uygulamalar:

- normalize edilmiş `Page.Slug`, `BlogPost.Slug` ve user email için unique constraint;
- page, section, media reference, menu ve author için foreign key ve bilinçli delete behavior;
- reorder, publish ve Refresh Token rotation gibi aggregate mutation'larında transaction;
- lost update olasılığı bulunan setting ve kayıtlarda optimistic concurrency token;
- Soft Delete'i yalnızca recovery, audit veya relation semantiği gerektirdiğinde kullanmak;
- UTC timestamp ve dokümante edilmiş time stratejisi;
- desteklenen deployment süreci ve rollback/recovery planıyla uyumlu migration'lar.

Olası index'ler: Page slug, BlogPost slug, blog publication query'leri için `(Status, PublishedAt)`, Media `CreatedAt` ve iletişim inbox query'leri için `(Status, CreatedAt)`. Index'ler gerçek query şekli, selectivity, ordering ve execution plan'a göre seçilmelidir. Her kolona index eklemek write cost ve storage'ı artırır; faydalı read garantilemez.

JSONB section'lar schema-aware Application validation gerektirir. Sık sorgulanan relational attribute'lar yalnızca kolaylık için JSONB içine gizlenmemelidir.

## 8. Performans ve Cache

Başlangıç performans stratejisi:

- seçilen rendering mode'a uygun Next.js Cache/revalidation mekanizmalarını kullanmak;
- Application/API Cache'i yalnızca kanıtlanmış read pattern'leri için eklemek;
- admin ve public koleksiyonları stable ordering ve bounded page size ile paginate etmek;
- N+1 query'leri önlemek ve yalnızca gerekli veriyi project etmek;
- uygun database constraint ve index'lerini korumak;
- pipeline olgunlaştıkça image ve response payload'larını optimize etmek;
- infrastructure eklemeden önce latency, query davranışı ve Cache etkinliğini ölçmek.

Başarılı publish, unpublish, slug değişikliği, theme/settings değişikliği, navigation değişikliği ve referans verilen media değişikliği etkilenen public Cache key'lerini invalidate veya revalidate etmelidir. Invalidation durable state değişikliğinden sonra gerçekleşmeli ve retry'a dayanıklı olmalıdır.

Redis, RabbitMQ, Kafka, Kubernetes ve microservice'ler MVP gereksinimi değildir. Ölçülen ölçek veya güvenilirlik gereksinimi oluşana kadar bunların deployment, failure-mode, observability ve local-development maliyetleri gerekçesizdir.

## 9. Doğrulama, Hata Yönetimi, Loglama ve Health Check

Frontend validation UX'i iyileştirir; güvenlik veya veri bütünlüğü sınırı değildir. Backend request validation zorunlu, merkezi ve tutarlı olmalı; Domain invariant enforcement ile tamamlanmalıdır. FluentValidation implementation sırasında değerlendirilebilir; mimari gereksinim belirli bir library değil tutarlı davranıştır.

API, merkezi exception handling ve ASP.NET Core ProblemDetails uyumlu response kullanır. Beklenen validation, conflict, not-found, authentication ve authorization failure'ları stable error semantics taşır. Production response stack trace veya database/provider ayrıntısı göstermez.

Backend `ILogger<T>` ve correlation/trace identifier içeren structured logging kullanır. Log'lar secret taşıyabilecek payload dump yerine eyleme dönük event field'ları içermelidir. Password, access token, Refresh Token, cookie, connection string, API secret ve hassas kişisel içerik loglanmaz. Serilog veya başka bir production sink daha sonra implementation tercihi olarak seçilebilir.

En az `GET /health`, bilinçli tanımlanmış API liveness/readiness durumunu bildirir; PostgreSQL ve storage bağlantısını hafif, non-destructive operasyonlarla kontrol eder. Operational olarak yararlıysa external service kontrolleri eklenebilir. Public Health Check response bağlantı veya topology ayrıntısı sızdırmamalıdır.

## 10. Test Stratejisi

Test yatırımı ham test sayısını değil riski izler.

### 10.1 Unit Test

Hızlı testler; slug normalization ve reserved route'lar, publish kuralları, component-setting validation, section reorder kuralları, media deletion protection, Refresh Token rotation/reuse davranışı, SEO fallback ve diğer iş invariant'ları gibi deterministic Domain/Application davranışlarını kapsar.

### 10.2 Integration Test

Integration Test'ler gerçek API'yi, persistence mapping'lerini, authentication/refresh akışlarını, page creation ve publish işlemlerini, public page filtering'i, concurrency/conflict durumlarını ve secure media upload davranışını kapsar. JSONB, constraint, transaction ve index başta olmak üzere gerçek PostgreSQL davranışını doğrulamak için yalnızca in-memory substitute yerine Testcontainers değerlendirilmelidir.

### 10.3 E2E

Playwright kritik kullanıcı yolculuğunu kapsar:

```text
Login -> Create Page -> Add Section -> Upload/Select Media
      -> Preview -> Publish -> Verify Public Website
```

Yüksek değerli negatif akışlara anonymous draft access, invalid upload, expired/reused Refresh Token ve publication validation failure dâhildir. E2E kapsamı, execution güvenilirliğini ve tanı kalitesini korumak için odaklı tutulur.

## 11. Yerel Geliştirme, CI/CD ve Operasyon

Local development dokümante edilmiş ve tekrarlanabilir olmalıdır. Daha sonraki fazda Docker Compose; PostgreSQL, API, Admin ve Web servislerini koordine edebilir. Daha basit ve eşdeğer olduğunda tüm development tool'larının container içinde çalışması zorunlu değildir.

Planlanan CI platformu GitHub Actions'tır. Pull request'ler şunları çalıştırmalıdır:

| Backend | Frontend |
|---|---|
| Restore | Reproducible dependency install |
| Tanımlı warning policy ile build | Lint |
| Unit Test ve Integration Test | Type check |
| Uygun olduğunda migration/schema check | Build ve ilgili testler |

Başarısız required check merge işlemini engeller. Production deployment; environment-specific configuration, HTTPS, secret management, migration prosedürü, database ve media backup/restore prosedürleri ile asgari logging/monitoring temelini kullanmalıdır. Yalnızca başarılı backup yeterli değildir; restore prosedürleri test edilmelidir.

## 12. Git ve İnceleme Akışı

`main` korunur ve doğrudan feature development için kullanılmaz. Akış:

```text
Feature Branch -> Small Commits -> Push -> Pull Request
               -> CI -> Human Review -> Merge
```

Önerilen adlar: `feature/authentication`, `feature/page-management`, `feature/page-sections`, `feature/component-registry` ve `feature/media-library`. Branch'ler kısa ömürlü ve dar kapsamlı olmalıdır. Production değişiklikleri human reviewer tarafından incelenmeli; author bulguları çözmeli ve CI'ı green tutmalıdır.

Conventional Commits benzeri prefix'ler kullanılabilir: `feat:`, `fix:`, `docs:`, `test:`, `refactor:` ve `chore:`. Commit message tutarlı amacı açıklamalıdır. Generated file, migration ve dependency update'leri de handwritten code ile aynı review disiplinine tabidir.

## 13. Tamamlanma Tanımı (Definition of Done)

Bir feature yalnızca uygulanabilir tüm koşullar karşılandığında tamamlanmıştır:

- kabul edilen requirement ve acceptance behavior uygulanmıştır;
- kod build olur ve ilgili automated test'ler geçer;
- frontend ve backend validation sorumlulukları tamamlanmıştır;
- authorization, data exposure, upload, logging ve diğer güvenlik etkileri değerlendirilmiştir;
- schema değişiklikleri review edilmiş migration, constraint ve operational note içerir;
- ilgili olduğunda performans/Cache etkisi ve invalidation davranışı anlaşılmıştır;
- secret veya hassas test verisi commit edilmemiştir;
- gerekli user, API, architecture veya operations dokümantasyonu güncellenmiştir;
- pull request human review almış ve tüm required CI check'leri green durumdadır;
- bilinen kritik regression yoktur ve production'da feature'ı teşhis etmeye yetecek observability sağlanmıştır.

## 14. Geliştirme Yol Haritası

Milestone'lar entegre yeteneği tanımlar; ilgisiz işleri büyük pull request'lerde birleştirme zorunluluğu oluşturmaz. Her faz küçük, review edilebilir değişikliklere ayrılmalıdır.

### Faz 0 — Planlama ve Repository Temeli

**Amaç:** Ürün implementation başlamadan kapsamı, mühendislik standartlarını ve repository sözleşmesini oluşturmak.

**Kapsam:** Implementation plan; daha sonra repository dokümantasyonu ve hijyeni, contribution/review beklentileri ve Git workflow. Bu görev yalnızca `implementation_plan.md` dosyasını teslim eder; README, `.gitignore` ve diğer foundation dosyaları ayrı çalışma gerektirir.

**Ana çıktılar:**

- onaylanmış `implementation_plan.md`;
- daha sonra: README, `.gitignore`, repository standartları, branch protection ve workflow dokümantasyonu.

**Önerilen feature branch'leri:** `docs/implementation-plan`, `chore/repository-foundation`.

**Çıkış kriterleri:** Kapsam ve non-goal'lar review edilebilir; architecture sınırları ve fazlar kabul edilmiştir; application'lar erken scaffold edilmeden repository standartları dokümante edilmiştir.

### Faz 1 — Backend ve Frontend Temeli

**Amaç:** Build edilebilir application temellerini oluşturmak ve uçtan uca bağlantıyı doğrulamak.

**Kapsam:** API/Application/Domain/Infrastructure projelerini içeren .NET solution; Next.js Admin ve Public Web; PostgreSQL/EF Core configuration; environment configuration ve temel Health Check.

**Ana çıktılar:** Build edilebilir backend ve frontend uygulamaları, ilk database connection/migration yaklaşımı, configuration validation, local setup talimatları ve temel health endpoint.

**Önerilen feature branch'leri:** `feature/backend-foundation`, `feature/admin-foundation`, `feature/public-web-foundation`, `feature/database-foundation`.

**Çıkış kriterleri:** Clean checkout yapılandırılıp build edilebilir; uygulamalar başlar; PostgreSQL connectivity ve health behavior doğrulanır; katman dependency'leri mimariye uyar; baseline CI çalışır.

**Hedef milestone:** `v0.1.0`

### Faz 2 — Kimlik Doğrulama ve Temel Ayarlar

**Amaç:** Admin sınırını güvenceye almak ve ilk global site configuration'ını sağlamak.

**Kapsam:** User/admin authentication, access/Refresh Token, hash'lenmiş token persistence, rotation, revocation, logout/logout-all, login Rate Limiting, browser security kararı ve Site Settings.

**Ana çıktılar:** Auth endpoint'leri ve UI, authorization temeli, token lifecycle, settings API/UI, migration'lar, threat-model kaydı ve automated test'ler.

**Önerilen feature branch'leri:** `feature/authentication`, `feature/refresh-token-rotation`, `feature/site-settings`.

**Çıkış kriterleri:** Protected route'lar unauthorized erişimi reddeder; refresh replay/revocation davranışı test edilmiştir; token/secret yönetimi review'dan geçer; settings concurrency/validation kontrolüyle persist edilir.

**Hedef milestone:** `v0.2.0`

### Faz 3 — Temel Sayfa Yönetimi

**Amaç:** Görsel section'lardan bağımsız, güvenilir page lifecycle yönetimi sunmak.

**Kapsam:** Page CRUD, duplicate, normalize/reserved slug kuralları, Draft/Published transition, SEO metadata, pagination, audit metadata ve gerekçeli deletion behavior.

**Ana çıktılar:** Admin page workflow'ları, API use case'leri, persistence constraint/index'leri, publication kuralları ve Unit Test/Integration Test.

**Önerilen feature branch'leri:** `feature/page-management`, `feature/page-slugs`, `feature/page-publishing`, `feature/page-seo`.

**Çıkış kriterleri:** Slug race condition'ları database tarafından engellenir; draft/public sınırları test edilmiştir; page list'leri paginated'dır; lifecycle ve deletion semantics dokümante edilmiştir.

**Hedef milestone:** `v0.3.0`

### Faz 4 — Section Builder ve Component Registry

**Amaç:** Doğrulanmış frontend varyantlarından kontrollü page composition sağlamak.

**Kapsam:** PageSections, JSONB settings, registry ve component variants, schema validation, add/edit/duplicate/delete, reorder ve hide/show.

**Ana çıktılar:** Section API'leri ve editor UI, ilk component seti, registry fallback behavior, reorder transaction, settings contract'ları ve testler.

**Önerilen feature branch'leri:** `feature/page-sections`, `feature/component-registry`, `feature/section-editor`, `feature/section-reordering`.

**Çıkış kriterleri:** Bilinmeyen key ve invalid settings güvenli biçimde hata verir; reorder yalnızca ordering state'i değiştirir; saklanan settings onaylı component'lerle render edilir; database kaynaklı executable code mümkün değildir.

**Hedef milestone:** `v0.4.0`

### Faz 5 — Medya, Tema ve Navigasyon

**Amaç:** Yeniden kullanılabilir asset'ler ile global presentation/navigation kontrollerini sağlamak.

**Kapsam:** Media Library, `IFileStorage`, local ve S3-compatible adapter, secure upload validation, usage-protected deletion, Theme Settings, header/footer menu ve nested navigation.

**Ana çıktılar:** Media API/UI, metadata ve reference tracking, storage configuration, theme token'ları, menu editor, public contract'lar ve security/integration test'leri.

**Önerilen feature branch'leri:** `feature/media-library`, `feature/file-storage`, `feature/theme-settings`, `feature/menu-management`.

**Çıkış kriterleri:** Malicious/invalid upload reddedilir; storage path provider-neutral'dır; kullanılan media yanlışlıkla silinemez; theme ve menu doğru validate ve persist edilir.

**Hedef milestone:** `v0.5.0`

### Faz 6 — Public Web Sitesi Renderer'ı

**Amaç:** Published content'i güvenli, responsive ve Cache-aware web sitesine dönüştürmek.

**Kapsam:** Dynamic route'lar, public API, registry rendering, responsive behavior, authorized preview, publish flow integration, draft protection, Cache/invalidation ve 404 behavior.

**Ana çıktılar:** Public route renderer, SEO metadata integration, preview authorization, Cache stratejisi ve invalidation hook'ları, error/404 UX ve E2E kapsamı.

**Önerilen feature branch'leri:** `feature/public-renderer`, `feature/page-preview`, `feature/publish-cache-invalidation`, `feature/public-routing`.

**Çıkış kriterleri:** Published page'ler responsive render edilir; anonymous draft retrieval testlerde imkânsızdır; preview protected ve non-cacheable'dır; content değişiklikleri ilgili Cache'i invalidate eder; missing/unpublished route doğru davranışı döndürür.

**Hedef milestone:** `v0.6.0`

### Faz 7 — İçerik Modülleri

**Amaç:** Temel editoryal ve ziyaretçi iletişim yeteneklerini tamamlamak.

**Kapsam:** Blog post, category, tag, rich text ve sanitization, SEO, sitemap, robots.txt, contact form ve contact inbox/status.

**Ana çıktılar:** Blog admin/public deneyimleri, sanitized content pipeline, paginated list'ler, SEO discovery file'ları, abuse-protected contact endpoint, message management ve isteğe bağlı email adapter.

**Önerilen feature branch'leri:** `feature/blog`, `feature/blog-taxonomy`, `feature/seo-discovery`, `feature/contact-form`, `feature/contact-messages`.

**Çıkış kriterleri:** Yalnızca uygun post'lar public olur; rich text kontrolsüz content çalıştıramaz; sitemap/robots kuralları doğrulanır; contact submission validate ve rate-limit edilir, güvenle render edilir ve operational olarak teşhis edilebilir.

**Hedef milestone:** `v0.7.0`

### Faz 8 — Kalite, Güvenlik ve Test

**Amaç:** Cross-cutting riskleri kapatmak ve kritik davranışların güvenilirliğini göstermek.

**Kapsam:** Validation/security review, merkezi error handling, structured logging, Health Check, concurrency control, performance review ve genişletilmiş Unit Test, Integration Test, E2E suite'leri.

**Ana çıktılar:** Threat/risk checklist, tutarlı ProblemDetails, trace correlation, log-redaction doğrulaması, gerçekçi PostgreSQL testleri, kritik akış E2E testleri ve dokümante residual risk'ler.

**Önerilen feature branch'leri:** `test/integration-suite`, `test/critical-e2e`, `feature/error-observability`, `hardening/security-review`.

**Çıkış kriterleri:** Kritik yolculuklar ve negatif authorization path'leri otomatik test edilir; bilinen kritik/yüksek çözülmemiş güvenlik sorunu yoktur; log/error hassas veri açığa çıkarmaz; health ve concurrency behavior doğrulanır; CI kararlıdır.

**Hedef milestone:** `v0.8.0`

### Faz 9 — DevOps, Dağıtım ve Sürüm

**Amaç:** Desteklenebilir, kurtarılabilir ve dokümante edilmiş production release üretmek.

**Kapsam:** Yararlı olduğu yerde Docker/Compose, GitHub Actions, branch protection, deployment, production configuration, HTTPS, backup/restore, temel logging/monitoring, architecture documentation, installation guide, final README ve release management.

**Ana çıktılar:** Deployment artifact ve runbook, korumalı CI/CD, secret/configuration matrix, migration procedure, test edilmiş backup/restore süreci, production check'leri, installation/architecture dokümanları ve release note'ları.

**Önerilen feature branch'leri:** `chore/docker`, `ci/github-actions`, `ops/production-deployment`, `docs/architecture-and-installation`, `release/v1.0.0`.

**Çıkış kriterleri:** Clean environment dokümante edilen release'i deploy edebilir; HTTPS ve secret'lar doğru yapılandırılır; rollback ve restore adımları prova edilmiştir; required check ve branch protection aktiftir; smoke test'ler geçer; dokümantasyon shipped system ile uyumludur.

**Hedef milestone:** `v1.0.0`

## 15. Gelecek Yol Haritası ve Açık v1 Kapsam Dışı Maddeler

Aşağıdakiler bilinçli olarak v1 kapsamı dışındadır. Bu tercih teslimat odağını korur ve doğrulanmamış ölçek ya da use case'ler için tasarım yapılmasını önler:

- multi-site ve multi-tenancy/SaaS;
- AI Website Generator;
- advanced free-form Drag & Drop;
- plugin system, plugin marketplace ve template marketplace;
- full revision history ve scheduled publishing;
- advanced RBAC ve 2FA;
- multi-language content;
- Next.js source project export;
- advanced analytics ve advanced observability stack;
- generic form builder;
- SEO score, broken-link checker ve advanced redirect manager;
- WebP conversion, image resizing, responsive variants ve thumbnail pipeline gibi advanced media processing;
- mega menu;
- distributed Redis Cache;
- message broker architecture;
- Kubernetes ve microservice'ler.

Gelecekteki işler user need, threat/scale evidence, migration impact ve açık bir architecture decision ile başlamalıdır. Ertelenmiş olmak, taahhüt edilmiş olmak anlamına gelmez.

## 16. Doğrulanması Gereken Temel Kararlar

Plan, bağlamları netleşene kadar bazı implementation seçimlerini bilinçli olarak açık bırakır:

- monorepo ayrıntıları ve kesin frontend/backend folder layout;
- route bazında Next.js rendering ve Cache stratejisi;
- CSRF/XSS ve deployment-origin analizinden sonra cookie/token topology;
- ilk registry schema'ları ve settings-version migration politikası;
- rich-text editor ve sanitization library;
- production S3-compatible storage ve e-posta provider'ları;
- preview token/session tasarımı;
- aggregate bazında Soft Delete politikası;
- hosting platformu, CDN, secret manager, monitoring ve backup servisleri;
- kesin Rate Limiting, upload limit, lifetime ve retention period değerleri.

Bu seçimler yapıldığında lightweight architecture decision record olarak kaydedilmelidir. Environment veya güvenlik yaklaşımına göre farklılaşabilen default'lar yapılandırılabilir olmalıdır.

## 17. Mühendislik İlkeleri

1. **Gereksiz karmaşıklıktan önce sadelik.** Mevcut gereksinimleri temiz biçimde karşılayan en küçük mimari seçilir.
2. **Security by design.** Authentication, authorization, untrusted content, upload ve secret yönetimi feature gereksinimi kabul edilir.
3. **Açık sınırlar.** Domain ve Application; PostgreSQL, S3, SMTP ve presentation ayrıntılarından bağımsız tutulur.
4. **Kritik davranışı test et.** Publication visibility, token lifecycle, upload, veri bütünlüğü ve içerik oluşturma yolculuğuna öncelik verilir.
5. **Kanıta göre optimize et.** Cache, queue, distributed system veya specialized infrastructure eklemeden önce ölçüm yapılır.
6. **CV süslemesi için teknoloji kullanma.** Her dependency ve platform, kabul edilebilir operasyonel maliyetle somut bir problemi çözmelidir.
7. **Sürdürülebilirlik.** Okunabilir contract, cohesive module, öngörülebilir migration ve değiştirilebilir adapter tercih edilir.
8. **Observability.** Hatalar güvenli structured log, trace correlation, health signal ve operasyon dokümantasyonuyla teşhis edilebilir kılınır.
9. **Production readiness.** Configuration, HTTPS, recovery, migration, monitoring ve failure behavior teslimat kararlarına dâhil edilir.
10. **Küçük, review edilebilir değişiklikler.** Anlaşılabilen, test ve revert edilebilen dar branch ve tutarlı commit kullanılır.
11. **Dokümantasyon geliştirme sürecinin parçasıdır.** Architecture, setup, API, operations ve kararlar shipped product ile uyumlu tutulur.
