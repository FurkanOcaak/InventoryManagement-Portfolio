import { test } from 'node:test'
import assert from 'node:assert/strict'
import { homeForRoles, canAccessNavigation, roles } from '../src/auth/roleRouting.ts'

for (const [role, home] of [[roles.admin, '/dashboard'], [roles.staff, '/stock'], [roles.customer, '/portal'], [roles.viewer, '/dashboard']]) {
  test(`${role} uses its authenticated role home`, () => assert.equal(homeForRoles([role]), home))
}
test('unknown roles fail closed and mixed roles have a stable home', () => {
  assert.equal(homeForRoles([]), '/access-denied')
  assert.equal(homeForRoles(['Untrusted']), '/access-denied')
  assert.equal(homeForRoles([roles.customer, roles.admin]), '/dashboard')
  assert.equal(homeForRoles([roles.viewer, roles.customer]), '/portal')
})
test('Customer navigation hides every management page', () => {
  for (const path of ['/dashboard', '/products', '/warehouses', '/customers', '/stock', '/movements', '/low-stock']) {
    assert.equal(canAccessNavigation(path, [roles.customer]), false)
  }
  assert.equal(canAccessNavigation('/portal', [roles.customer]), true)
})
test('staff and viewer cannot see customers; Admin retains existing navigation', () => {
  for (const role of [roles.staff, roles.viewer]) {
    assert.equal(canAccessNavigation('/customers', [role]), false)
    assert.equal(canAccessNavigation('/stock', [role]), true)
    assert.equal(canAccessNavigation('/portal', [role]), false)
  }
  assert.equal(canAccessNavigation('/customers', [roles.admin]), true)
  assert.equal(canAccessNavigation('/products', [roles.admin]), true)
  assert.equal(canAccessNavigation('/dashboard', []), false)
})
