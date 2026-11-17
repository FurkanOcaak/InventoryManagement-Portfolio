import { ArrowLeft, MapPinOff } from 'lucide-react'
import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return <section className="not-found"><span className="empty-icon"><MapPinOff size={30} aria-hidden="true" /></span><span className="eyebrow">HATA 404</span><h1>Bu sayfayı bulamadık</h1><p>Adres değişmiş veya sayfa kaldırılmış olabilir.<br />Çalışma alanınızdan devam edebilirsiniz.</p><Link className="primary-link" to="/dashboard"><ArrowLeft size={17} aria-hidden="true" />Genel bakışa dön</Link></section>
}
