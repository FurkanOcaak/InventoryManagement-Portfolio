export const roles = { admin: 'Admin', staff: 'WarehouseStaff', customer: 'Customer', viewer: 'Viewer' } as const
export const operationalRoles = [roles.admin, roles.staff, roles.viewer]

export function homeForRoles(userRoles: readonly string[]): string {
  if (userRoles.includes(roles.admin)) return '/dashboard'
  if (userRoles.includes(roles.staff)) return '/stock'
  if (userRoles.includes(roles.customer)) return '/portal'
  if (userRoles.includes(roles.viewer)) return '/dashboard'
  return '/access-denied'
}

export function canAccessNavigation(path: string, userRoles: readonly string[]): boolean {
  if (path === '/portal' || path.startsWith('/portal/')) return userRoles.includes(roles.customer)
  if (path === '/orders' || path.startsWith('/orders/')) return userRoles.includes(roles.admin)
  if (path === '/customers') return userRoles.includes(roles.admin)
  return userRoles.some((role) => operationalRoles.includes(role as typeof operationalRoles[number]))
}
