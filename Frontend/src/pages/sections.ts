import { ArrowLeftRight, Boxes, Building2, ClipboardList, Package, TriangleAlert, Users } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

export interface InventorySection {
  path: string
  title: string
  eyebrow: string
  description: string
  icon: LucideIcon
  contentTitle: string
  emptyTitle: string
  emptyDescription: string
  details: { title: string; description: string }[]
}

export const sections: InventorySection[] = [
  {
    path: '/products', title: 'Ürünler', eyebrow: 'ENVANTER YÖNETİMİ', icon: Package,
    description: 'Ürün kataloğunuzu tek bir yerden takip edin.',
    contentTitle: 'Ürün kataloğu', emptyTitle: 'Ürün kataloğunuz burada yer alacak',
    emptyDescription: 'Ürün bilgileri, veri bağlantısı tamamlandığında bu alanda görüntülenecek.',
    details: [
      { title: 'Ürün bilgileri', description: 'Ürünlerinize ait temel bilgiler tek bir katalogda.' },
      { title: 'Stok görünürlüğü', description: 'Ürün bazında stok takibi için ortak bir görünüm.' },
      { title: 'Düzenli kayıtlar', description: 'Envanter operasyonlarınız için tutarlı ürün kayıtları.' },
    ],
  },
  {
    path: '/warehouses', title: 'Depolar', eyebrow: 'ENVANTER YÖNETİMİ', icon: Building2,
    description: 'Depolarınızı ve envanterinizin dağılımını takip edin.',
    contentTitle: 'Depo listesi', emptyTitle: 'Depolarınız burada yer alacak',
    emptyDescription: 'Depo kayıtları, veri bağlantısı tamamlandığında bu alanda görüntülenecek.',
    details: [
      { title: 'Depo bilgileri', description: 'Depo kayıtlarınız için merkezi bir görünüm.' },
      { title: 'Depo bazında stok', description: 'Ürünlerin hangi depoda bulunduğunu takip edin.' },
      { title: 'Ortak envanter', description: 'Depolar arasındaki stok dağılımını bir arada görün.' },
    ],
  },
  {
    path: '/stock', title: 'Stok İşlemleri', eyebrow: 'STOK YÖNETİMİ', icon: Boxes,
    description: 'Stok giriş ve çıkışları için merkezi çalışma alanınız.',
    contentTitle: 'Stok çalışma alanı', emptyTitle: 'Stok işlemleri için alanınız hazır',
    emptyDescription: 'Stok listesi ile giriş ve çıkış işlemleri, veri bağlantısı tamamlandıktan sonra kullanılabilecek.',
    details: [
      { title: 'Stok girişi', description: 'Depolara eklenen ürünlerin kayıtlarını yönetin.' },
      { title: 'Stok çıkışı', description: 'Depolardan çıkan ürünlerin kayıtlarını takip edin.' },
      { title: 'Güncel stok', description: 'Ürün ve depo bazındaki stok miktarlarını inceleyin.' },
    ],
  },
  {
    path: '/movements', title: 'Stok Hareketleri', eyebrow: 'STOK YÖNETİMİ', icon: ArrowLeftRight,
    description: 'Envanterinizdeki giriş ve çıkışların izini sürün.',
    contentTitle: 'Hareket geçmişi', emptyTitle: 'Stok hareketleri burada görüntülenecek',
    emptyDescription: 'Giriş ve çıkış kayıtları, veri bağlantısı tamamlandığında bu alanda görüntülenecek.',
    details: [
      { title: 'Giriş kayıtları', description: 'Depolarınıza yapılan stok girişlerinin geçmişi.' },
      { title: 'Çıkış kayıtları', description: 'Depolarınızdan yapılan stok çıkışlarının geçmişi.' },
      { title: 'İşlem takibi', description: 'Stok değişimlerini tek bir yerde inceleyin.' },
    ],
  },
  {
    path: '/low-stock', title: 'Düşük Stok', eyebrow: 'STOK YÖNETİMİ', icon: TriangleAlert,
    description: 'Kritik seviyedeki stoklara zamanında odaklanın.',
    contentTitle: 'Kritik stok takibi', emptyTitle: 'Düşük stok bilgileri burada yer alacak',
    emptyDescription: 'Kritik stok durumu henüz bilinmiyor. Uyarılar, veri bağlantısı tamamlandığında görüntülenecek.',
    details: [
      { title: 'Kritik ürünler', description: 'Stok seviyesi dikkat gerektiren ürünleri inceleyin.' },
      { title: 'Depo görünümü', description: 'Kritik stokların bulunduğu depoları takip edin.' },
      { title: 'Tedarik planlaması', description: 'Stok uyarılarınıza göre ihtiyaçlarınızı değerlendirin.' },
    ],
  },
  {
    path: '/customers', title: 'Müşteriler', eyebrow: 'İŞ ORTAKLARI', icon: Users,
    description: 'Müşteri kayıtlarınızı düzenli ve erişilebilir tutun.',
    contentTitle: 'Müşteri listesi', emptyTitle: 'Müşterileriniz burada yer alacak',
    emptyDescription: 'Müşteri kayıtları, veri bağlantısı tamamlandığında bu alanda görüntülenecek.',
    details: [
      { title: 'Müşteri bilgileri', description: 'Müşteri kayıtlarınız için ortak bir çalışma alanı.' },
      { title: 'İletişim bilgileri', description: 'Müşterilerinize ait iletişim bilgilerini bir arada görün.' },
      { title: 'Düzenli kayıtlar', description: 'İş ilişkilerinizi tutarlı müşteri kayıtlarıyla destekleyin.' },
    ],
  },
]

export const navigation = [
  { title: 'Genel Bakış', path: '/dashboard', icon: ClipboardList },
  { title: 'Müşteri Portalı', path: '/portal', icon: Users },
  { title: 'Ürün Kataloğu', path: '/portal/products', icon: Package },
  { title: 'Sepetim', path: '/portal/cart', icon: Boxes },
  { title: 'Siparişlerim', path: '/portal/orders', icon: ClipboardList },
  { title: 'Siparişler', path: '/orders', icon: ClipboardList },
  ...sections,
]
