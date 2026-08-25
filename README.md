# NovaCMS

NovaCMS, teknik bilgisi sınırlı kullanıcıların modern, responsive ve SEO uyumlu web siteleri oluşturup yönetebilmesi için planlanan modüler bir içerik yönetim sistemi ve section-based website builder projesidir.

## Proje Amacı

Projenin amacı; içerik yönetimi, kontrollü sayfa kompozisyonu ve güvenli yayınlama süreçlerini sade, sürdürülebilir ve production odaklı bir ürün altında birleştirmektir. İlk sürümde her NovaCMS kurulumu tek bir web sitesini yönetecektir.

## Mimari Özet

Backend, açık modül ve katman sınırlarına sahip bir Modular Monolith olarak tasarlanmaktadır. Admin paneli ile public web sitesi ayrı Next.js uygulamaları olacak; iş kuralları ve veri erişimi ASP.NET Core Web API üzerinden yönetilecektir. Public renderer, veritabanında çalıştırılabilir frontend kodu saklamak yerine doğrulanmış section yapılandırmalarını kontrollü bir Component Registry aracılığıyla bilinen bileşenlere eşleyecektir.

## Teknoloji Yığını

- Backend: .NET 10, ASP.NET Core Web API
- Frontend: Next.js, TypeScript, Tailwind CSS
- Veritabanı: PostgreSQL, Entity Framework Core
- Mimari yaklaşım: Modular Monolith
- Planlanan test araçları: .NET test altyapısı, Integration Test, Playwright
- Planlanan CI/CD: GitHub Actions

## Geliştirme Durumu

NovaCMS aktif geliştirme aşamasındadır. Şu anda Faz 0 kapsamında planlama ve repository temeli hazırlanmaktadır; uygulama özellikleri henüz geliştirilmiş veya kullanıma hazır değildir.

Ayrıntılı kapsam, mimari kararlar ve geliştirme yol haritası için [uygulama planını](implementation_plan.md) inceleyin.

## Kurulum

Uygulama temelleri ve çalıştırma gereksinimleri sonraki fazlarda oluşturulacağı için kurulum talimatları henüz mevcut değildir. Doğrulanmış yerel geliştirme ve kurulum adımları ilgili fazlarda bu dokümana eklenecektir.
