import { roles, useAuth, homeForRoles } from '../../auth/AuthContext'
import { canAccessNavigation } from '../../auth/roleRouting'
import { ArrowUpRight, Box, Layers3, X } from 'lucide-react'
import { NavLink } from 'react-router-dom'
import { navigation } from '../../pages/sections'

export function Sidebar({ onNavigate, mobile = false }: { onNavigate?: () => void; mobile?: boolean }) {
  const { user, busy, logout } = useAuth()
  const roleLabel = user?.roles.includes(roles.admin) ? 'Yönetici' : user?.roles.includes(roles.staff) ? 'Depo Personeli' : user?.roles.includes(roles.customer) ? 'Müşteri' : user?.roles.includes(roles.viewer) ? 'Görüntüleyici' : 'Yetki tanımlanmamış'
  return <div className="sidebar-content">
    <div className="brand-row"><NavLink to={homeForRoles(user?.roles ?? [])} className="brand" onClick={onNavigate} aria-label="InventoryManagement ana sayfa"><span className="brand-symbol"><Layers3 size={24} aria-hidden="true" /></span><span>Inventory<span className="brand-secondary">Management</span></span></NavLink>{mobile && <button className="icon-button sidebar-close" onClick={onNavigate} aria-label="Menüyü kapat"><X size={20} /></button>}</div>
    <div className="workspace-label"><span className="workspace-icon"><Box size={18} aria-hidden="true" /></span><div><strong>Envanter yönetimi</strong><span>Yönetim paneli</span></div></div>
    <nav aria-label="Ana menü"><p className="nav-label">ÇALIŞMA ALANI</p><div className="nav-links">{navigation.filter((item) => canAccessNavigation(item.path, user?.roles ?? [])).map(({ path, title, icon: Icon }) => <NavLink key={path} to={path} onClick={onNavigate} className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}><Icon size={19} strokeWidth={1.7} aria-hidden="true" /><span>{title}</span><span className="nav-active-dot" /></NavLink>)}</div></nav>
    <div className="sidebar-bottom"><section className="sidebar-session" aria-label="Oturum bilgileri"><span className="sidebar-session-role">{roleLabel}</span><p>{user?.email ?? 'Oturum açık'}</p><button type="button" disabled={busy} onClick={() => void logout()}>{busy ? 'İşlem sürüyor...' : 'Çıkış Yap'}</button></section><div className="sidebar-note"><span className="note-mark" /><strong>Her şey tek bir yerde.</strong><p>Ürünler, depolar ve stoklar için ortak çalışma alanınız.</p><NavLink to={homeForRoles(user?.roles ?? [])} onClick={onNavigate}>Çalışma alanına git<ArrowUpRight size={15} aria-hidden="true" /></NavLink></div><p className="sidebar-footer">InventoryManagement<span>ENVANTER & OPERASYON</span></p></div>
  </div>
}
