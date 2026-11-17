import { test, afterEach } from 'node:test'
import assert from 'node:assert/strict'
import { apiFetch, ApiError, resetApiSession, subscribeApi } from '../src/api/apiFetch.ts'

const originalFetch = globalThis.fetch
afterEach(() => { globalThis.fetch = originalFetch; resetApiSession() })
const json = (data, status = 200) => Response.json(data, { status })
const tokenResponse = () => json({ requestToken: 'test-only-token', headerName: 'X-CSRF-TOKEN' })

test('GET uses same-origin cookies and preserves the caller signal without requesting CSRF', async () => {
  const controller = new AbortController()
  globalThis.fetch = async (path, init) => {
    assert.equal(path, '/api/products')
    assert.equal(init.credentials, 'same-origin')
    assert.equal(init.signal, controller.signal)
    assert.equal(init.headers.has('X-CSRF-TOKEN'), false)
    return json([])
  }
  assert.deepEqual(await (await apiFetch('/api/products', { signal: controller.signal })).json(), [])
})

test('all four mutation verbs use a shared CSRF token and preserve body and headers', async () => {
  let tokens = 0, writes = 0
  globalThis.fetch = async (path, init) => {
    assert.equal(init.credentials, 'same-origin')
    if (path === '/api/auth/csrf') { tokens++; return tokenResponse() }
    writes++
    assert.equal(init.headers.get('X-CSRF-TOKEN'), 'test-only-token')
    assert.equal(init.headers.get('Content-Type'), 'application/json')
    assert.equal(init.body, '{}')
    return new Response(null, { status: 204 })
  }
  await Promise.all(['POST', 'PUT', 'PATCH', 'DELETE'].map(async (method) => {
    assert.equal((await apiFetch('/api/products', { method, headers: { 'Content-Type': 'application/json' }, body: '{}' })).status, 204)
  }))
  assert.equal(tokens, 1); assert.equal(writes, 4)
})

test('validation 400 and conflict 409 remain available to existing field error handlers', async () => {
  for (const status of [400, 409]) {
    const data = { errors: { Code: ['Doğrulama hatası'] } }
    globalThis.fetch = async () => json(data, status)
    const response = await apiFetch('/api/products')
    assert.equal(response.status, status)
    assert.deepEqual(await response.json(), data)
  }
})

test('business 401 emits session expiration while login 401 and bootstrap 401 do not', async () => {
  const events = []
  const unsubscribe = subscribeApi((event) => events.push(event))
  try {
    globalThis.fetch = async (path) => path === '/api/auth/csrf' ? tokenResponse() : new Response(null, { status: 401 })
    assert.equal((await apiFetch('/api/auth/me')).status, 401)
    assert.equal((await apiFetch('/api/auth/login', { method: 'POST' })).status, 401)
    assert.equal(events.length, 0)
    await assert.rejects(apiFetch('/api/products'), (error) => error instanceof ApiError && error.status === 401)
    assert.equal(events[0].type, 'unauthorized')
  } finally { unsubscribe() }
})

test('403 and 429 publish actionable notices without expiring the session or retrying', async () => {
  for (const status of [403, 429]) {
    const events = [], unsubscribe = subscribeApi((event) => events.push(event))
    let writes = 0
    try {
      globalThis.fetch = async (path) => { if (path.endsWith('/csrf')) return tokenResponse(); writes++; return json({}, status) }
      await assert.rejects(apiFetch('/api/products', { method: 'POST' }), (error) => error.status === status)
      assert.equal(writes, 1)
      assert.equal(events.at(-1).type, 'notice')
    } finally { unsubscribe(); resetApiSession() }
  }
})

test('CSRF 400 invalidates token for the next user action and never repeats the failed write', async () => {
  let tokens = 0, writes = 0
  globalThis.fetch = async (path) => {
    if (path.endsWith('/csrf')) { tokens++; return tokenResponse() }
    writes++
    return writes === 1 ? json({ title: 'İstek doğrulanamadı.' }, 400) : new Response(null, { status: 204 })
  }
  await assert.rejects(apiFetch('/api/products', { method: 'POST' }), (error) => error.status === 400)
  assert.equal(writes, 1); assert.equal(tokens, 1)
  await apiFetch('/api/products', { method: 'POST' })
  assert.equal(writes, 2); assert.equal(tokens, 2)
})

test('network and server failures do not retry a mutation', async () => {
  for (const network of [false, true]) {
    let writes = 0
    globalThis.fetch = async (path) => {
      if (path.endsWith('/csrf')) return tokenResponse()
      writes++
      if (network) throw new TypeError('Test network failure')
      return json({}, 500)
    }
    if (network) await assert.rejects(apiFetch('/api/products', { method: 'POST' }), TypeError)
    else assert.equal((await apiFetch('/api/products', { method: 'POST' })).status, 500)
    assert.equal(writes, 1)
    resetApiSession()
  }
})

test('aborting while a shared CSRF fetch is pending prevents the mutation', async () => {
  let resolve, writes = 0
  globalThis.fetch = async (path) => {
    if (path.endsWith('/csrf')) return new Promise((done) => { resolve = done })
    writes++; return new Response(null, { status: 204 })
  }
  const controller = new AbortController()
  const pending = apiFetch('/api/products', { method: 'POST', signal: controller.signal })
  controller.abort()
  await assert.rejects(pending, (error) => error.name === 'AbortError')
  resolve(tokenResponse())
  await new Promise((done) => setImmediate(done))
  assert.equal(writes, 0)
})

test('resetting the session discards in-flight token results and old response results', async () => {
  let resolve
  globalThis.fetch = async () => new Promise((done) => { resolve = done })
  const pending = apiFetch('/api/products')
  resetApiSession()
  resolve(json([]))
  await assert.rejects(pending, (error) => error.name === 'AbortError')
})

test('invalid CSRF JSON and contracts prevent any write', async () => {
  for (const response of [new Response('<html>'), json({}), json({ requestToken: 'test', headerName: 'Other' })]) {
    let writes = 0
    globalThis.fetch = async (path) => { if (path.endsWith('/csrf')) return response; writes++; return json({}) }
    await assert.rejects(apiFetch('/api/products', { method: 'POST' }))
    assert.equal(writes, 0)
    resetApiSession()
  }
})

test('apiFetch rejects addresses outside the same-origin API', async () => {
  globalThis.fetch = async () => assert.fail('Unexpected fetch')
  for (const path of ['https://example.test/api/products', '//example.test/api/products', '/api/\\example.test']) {
    await assert.rejects(apiFetch(path))
  }
})

test('a signal already aborted does not start even the CSRF request', async () => {
  globalThis.fetch = async () => assert.fail('Unexpected fetch')
  const controller = new AbortController()
  controller.abort()
  await assert.rejects(apiFetch('/api/products', { method: 'POST', signal: controller.signal }), (error) => error.name === 'AbortError')
})
