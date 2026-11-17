import { test, afterEach } from 'node:test'
import assert from 'node:assert/strict'
import { checkoutAttempt, pendingCheckout, finishCheckout, quantityValue, commerceRequest, CommerceRequestError, jsonRequest } from '../src/commerce/commerceApi.ts'
import { resetApiSession } from '../src/api/apiFetch.ts'
import { canAccessNavigation, roles } from '../src/auth/roleRouting.ts'

const originalFetch = globalThis.fetch
afterEach(() => { globalThis.fetch = originalFetch; resetApiSession() })
function storage() { const map = new Map(); return { getItem: (key) => map.get(key) ?? null, setItem: (key, value) => map.set(key, value), removeItem: (key) => map.delete(key), map } }
const key = '00000000-0000-4000-8000-000000000001'
const version = '00000000-0000-4000-8000-000000000002'

test('quantity bounds reject decimals, negative and overflow inputs', () => {
  for (const input of ['', '0', '-1', '1.5', '1e3', '10001', '2147483648', '999999999999999999999']) assert.equal(quantityValue(input), null)
  assert.equal(quantityValue('1'), 1); assert.equal(quantityValue('10000'), 10000)
})
test('checkout preserves the original key and cart version through refresh and unknown results', () => {
  const state = storage()
  const first = checkoutAttempt(state, 'customer-a', version, () => key)
  assert.deepEqual(pendingCheckout(state, 'customer-a'), first)
  assert.deepEqual(checkoutAttempt(state, 'customer-a', 'new-cart-version', () => assert.fail('must not create another key')), first)
  assert.equal(state.map.size, 1)
  assert.deepEqual(Object.keys(first).sort(), ['cartVersion', 'idempotencyKey'])
})
test('checkout keys are isolated by user and removed after an acknowledged order', () => {
  const state = storage()
  checkoutAttempt(state, 'a', version, () => key)
  assert.equal(pendingCheckout(state, 'b'), null)
  checkoutAttempt(state, 'b', version, () => key)
  finishCheckout(state, 'a')
  assert.equal(pendingCheckout(state, 'a'), null); assert.ok(pendingCheckout(state, 'b'))
})
test('malformed pending checkout does not become a new automatic order attempt', () => {
  const state = storage(); state.setItem('inventory.checkout.a', '{broken')
  assert.throws(() => checkoutAttempt(state, 'a', version, () => assert.fail('no new key')))
  state.setItem('inventory.checkout.a', JSON.stringify({ idempotencyKey: 'bad', cartVersion: version }))
  assert.throws(() => pendingCheckout(state, 'a'))
})
test('all portal subpages and all administrative order paths use distinct role navigation', () => {
  for (const path of ['/portal/products', '/portal/cart', '/portal/orders', '/portal/orders/' + key]) {
    assert.equal(canAccessNavigation(path, [roles.customer]), true)
    for (const role of [roles.admin, roles.staff, roles.viewer]) assert.equal(canAccessNavigation(path, [role]), false)
  }
  for (const path of ['/orders', '/orders/' + key]) {
    assert.equal(canAccessNavigation(path, [roles.admin]), true)
    for (const role of [roles.customer, roles.staff, roles.viewer]) assert.equal(canAccessNavigation(path, [role]), false)
  }
})
test('checkout network failure never retries; an explicit retry preserves payload and CSRF', async () => {
  const state = storage(); const writes = []
  globalThis.fetch = async (path, init) => {
    if (path === '/api/auth/csrf') return Response.json({ requestToken: 'fixture-csrf', headerName: 'X-CSRF-TOKEN' })
    assert.equal(init.headers.get('X-CSRF-TOKEN'), 'fixture-csrf')
    writes.push(init.body)
    if (writes.length === 1) throw new TypeError('Fixture network failure')
    return Response.json({ id: key, status: 'Pending' })
  }
  const first = checkoutAttempt(state, 'a', version, () => key)
  await assert.rejects(commerceRequest('/api/customer-portal/orders', jsonRequest('POST', first)))
  assert.equal(writes.length, 1)
  const second = checkoutAttempt(state, 'a', version, () => assert.fail('no second key'))
  await commerceRequest('/api/customer-portal/orders', jsonRequest('POST', second))
  assert.deepEqual(writes, [JSON.stringify(first), JSON.stringify(first)])
})
test('business conflicts and invalid quantities expose a readable error without repeating writes', async () => {
  for (const status of [400, 409]) {
    resetApiSession(); let writes = 0
    globalThis.fetch = async (path) => path === '/api/auth/csrf'
      ? Response.json({ requestToken: 'fixture', headerName: 'X-CSRF-TOKEN' })
      : (writes++, Response.json({ title: 'Sepetinizi güncelleyin.' }, { status }))
    await assert.rejects(commerceRequest('/api/customer-portal/orders', jsonRequest('POST', {})),
      (error) => error instanceof CommerceRequestError && error.status === status && error.message === 'Sepetinizi güncelleyin.')
    assert.equal(writes, 1)
  }
})
test('foreign or missing order returns a safe not-found message', async () => {
  globalThis.fetch = async () => Response.json({ title: 'Kayıt bulunamadı' }, { status: 404 })
  await assert.rejects(commerceRequest('/api/customer-portal/orders/' + key),
    (error) => error instanceof CommerceRequestError && error.status === 404 && error.message === 'Sipariş veya kayıt bulunamadı.')
})
