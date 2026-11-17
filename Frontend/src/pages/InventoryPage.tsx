import { Check } from 'lucide-react'
import { Card, ConnectionNotice, EmptyState, PageHeading } from '../components/ui/PageElements'
import type { InventorySection } from './sections'

export function InventoryPage({ section }: { section: InventorySection }) {
  return <><PageHeading eyebrow={section.eyebrow} title={section.title} description={section.description} /><ConnectionNotice /><Card title={section.contentTitle} icon={section.icon} className="inventory-card"><EmptyState icon={section.icon} title={section.emptyTitle} description={section.emptyDescription} /><div className="inventory-card-footer">Bu alan veri bağlantısı tamamlandığında kullanılabilecek.</div></Card><section className="page-details" aria-label={`${section.title} kapsamı`}>{section.details.map((detail) => <article key={detail.title}><span className="detail-icon"><Check size={16} aria-hidden="true" /></span><div><h2>{detail.title}</h2><p>{detail.description}</p></div></article>)}</section></>
}
