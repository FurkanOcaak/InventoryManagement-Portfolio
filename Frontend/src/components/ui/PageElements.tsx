import { ArrowUpRight, Database, Unplug } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'

export function PageHeading({ eyebrow, title, description }: { eyebrow: string; title: string; description: string }) {
  return <header className="page-heading"><span className="eyebrow">{eyebrow}</span><h1>{title}</h1><p>{description}</p></header>
}

export function ConnectionNotice() {
  return <aside className="connection-notice" aria-label="Veri bağlantısı durumu"><span className="notice-icon"><Unplug size={18} aria-hidden="true" /></span><div><strong>Veri bağlantısı bekleniyor</strong><p>Veriler API bağlantısından sonra görüntülenecek.</p></div><span className="status-pill"><span />Henüz bağlı değil</span></aside>
}

export function Card({ title, icon: Icon, link, children, className = '' }: {
  title: string; icon: LucideIcon; link?: { to: string; label: string }; children: ReactNode; className?: string
}) {
  return <section className={`card ${className}`}><div className="card-heading"><h2><Icon size={19} aria-hidden="true" />{title}</h2>{link && <Link className="text-link" to={link.to}>{link.label}<ArrowUpRight size={15} aria-hidden="true" /></Link>}</div>{children}</section>
}

export function EmptyState({ icon: Icon = Database, title, description, compact = false }: {
  icon?: LucideIcon; title: string; description: string; compact?: boolean
}) {
  return <div className={`empty-state ${compact ? 'compact' : ''}`}><div className="empty-icon"><Icon size={26} strokeWidth={1.5} aria-hidden="true" /></div><h3>{title}</h3><p>{description}</p></div>
}
