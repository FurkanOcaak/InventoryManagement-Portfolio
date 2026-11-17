import { AuthProvider } from './auth/AuthProvider'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { operationalRoles, roles, useAuth, homeForRoles } from './auth/AuthContext'
import { CustomerPortalPage } from './pages/CustomerPortalPage'
import { PortalProductsPage } from './pages/PortalProductsPage'
import { PortalCartPage } from './pages/PortalCartPage'
import { OrdersPage, OrderDetailPage } from './pages/OrdersPages'
import { LoginPage } from './pages/LoginPage'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AppLayout } from './components/layout/AppLayout'
import { DashboardPage } from './pages/DashboardPage'
import { InventoryPage } from './pages/InventoryPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { ProductsPage } from './pages/ProductsPage'
import { WarehousesPage } from './pages/WarehousesPage'
import { StockPage } from './pages/StockPage'
import { MovementsPage } from './pages/MovementsPage'
import { LowStockPage } from './pages/LowStockPage'
import { CustomersPage } from './pages/CustomersPage'
import { sections } from './pages/sections'

function RoleHome() {
  const { user } = useAuth()
  return <Navigate to={homeForRoles(user?.roles ?? [])} replace />
}

export default function App() {
  return <BrowserRouter><AuthProvider><Routes>
    <Route path="login" element={<LoginPage />} />
    <Route element={<ProtectedRoute />}>
      <Route element={<AppLayout />}>
        <Route index element={<RoleHome />} />
        <Route path="access-denied" element={<section className="auth-denied"><h1>Erişim engellendi</h1><p>Hesabınıza uygun bir çalışma alanı tanımlanmamış.</p></section>} />
        <Route element={<ProtectedRoute allowedRoles={[roles.customer]} />}>
          <Route path="portal" element={<CustomerPortalPage />} />
          <Route path="portal/products" element={<PortalProductsPage />} />
          <Route path="portal/cart" element={<PortalCartPage />} />
          <Route path="portal/orders" element={<OrdersPage key="customer" />} />
          <Route path="portal/orders/:id" element={<OrderDetailPage />} />
        </Route>
        <Route element={<ProtectedRoute allowedRoles={operationalRoles} />}>
          <Route path="dashboard" element={<DashboardPage />} />
          <Route path="products" element={<ProductsPage />} />
          <Route path="warehouses" element={<WarehousesPage />} />
          <Route path="stock" element={<StockPage />} />
          <Route path="movements" element={<MovementsPage />} />
          <Route path="low-stock" element={<LowStockPage />} />
          {sections.filter((section) => section.path !== '/products' && section.path !== '/warehouses' && section.path !== '/stock' && section.path !== '/movements' && section.path !== '/low-stock' && section.path !== '/customers').map((section) => <Route key={section.path} path={section.path} element={<InventoryPage section={section} />} />)}
          <Route path="*" element={<NotFoundPage />} />
        </Route>
        <Route element={<ProtectedRoute allowedRoles={[roles.admin]} />}>
          <Route path="customers" element={<CustomersPage />} />
          <Route path="orders" element={<OrdersPage key="admin" admin />} />
          <Route path="orders/:id" element={<OrderDetailPage admin />} />
        </Route>
      </Route>
    </Route>
  </Routes></AuthProvider></BrowserRouter>
}
