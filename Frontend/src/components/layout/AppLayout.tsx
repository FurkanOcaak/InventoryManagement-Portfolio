import { useAuth } from '../../auth/AuthContext'
import { useEffect, useRef, useState } from 'react'
import { ChevronRight, Menu, PanelLeft } from 'lucide-react'
import { Outlet, useLocation } from 'react-router-dom'
import { navigation } from '../../pages/sections'
import { Sidebar } from './Sidebar'

export function AppLayout() {
  const { notice } = useAuth()
  const [menuOpen, setMenuOpen] = useState(false)
  const dialogRef = useRef<HTMLDialogElement>(null)
  const mainRef = useRef<HTMLElement>(null)
  const { pathname } = useLocation()
  const title = navigation.find((item) => item.path === pathname)?.title
    ?? (pathname.startsWith('/orders/') || pathname.startsWith('/portal/orders/') ? 'Sipariş detayı' : 'Sayfa bulunamadı')

  useEffect(() => {
    document.title = `${title} | InventoryManagement`
    mainRef.current?.focus({ preventScroll: true })
    window.scrollTo(0, 0)
  }, [pathname, title])

  useEffect(() => {
    const dialog = dialogRef.current
    if (menuOpen && !dialog?.open) dialog?.showModal()
    if (!menuOpen && dialog?.open) dialog.close()
    document.body.classList.toggle('menu-open', menuOpen)
    return () => document.body.classList.remove('menu-open')
  }, [menuOpen])

  useEffect(() => {
    const desktop = window.matchMedia('(min-width: 1024px)')
    const closeOnDesktop = () => { if (desktop.matches) setMenuOpen(false) }
    desktop.addEventListener('change', closeOnDesktop)
    return () => desktop.removeEventListener('change', closeOnDesktop)
  }, [])

  return <div className="app-shell">
    <a className="skip-link" href="#main-content">Ana içeriğe geç</a>
    <aside className="desktop-sidebar"><Sidebar /></aside>
    <dialog ref={dialogRef} id="mobile-navigation" className="mobile-sidebar" aria-label="Gezinme menüsü" onCancel={() => setMenuOpen(false)} onClose={() => setMenuOpen(false)} onClick={(event) => { if (event.target === event.currentTarget) setMenuOpen(false) }}><Sidebar mobile onNavigate={() => setMenuOpen(false)} /></dialog>
    <div className="app-body"><header className="topbar"><div className="topbar-location"><button className="icon-button mobile-toggle" aria-label="Menüyü aç" aria-expanded={menuOpen} aria-controls="mobile-navigation" onClick={() => setMenuOpen(true)}><Menu size={22} /></button><PanelLeft className="desktop-marker" size={18} aria-hidden="true" /><span className="breadcrumb-parent">Çalışma alanı</span><ChevronRight size={14} className="breadcrumb-chevron" aria-hidden="true" /><span className="current-page">{title}</span></div><span className="topbar-caption">ENVANTER YÖNETİM PANELİ</span></header>
      <main id="main-content" ref={mainRef} tabIndex={-1}>{notice && <p className="auth-notice" role="alert">{notice}</p>}<Outlet /></main><footer className="content-footer"><span>InventoryManagement</span><span>Düzenli envanter. Güçlü operasyon.</span></footer>
    </div>
  </div>
}
