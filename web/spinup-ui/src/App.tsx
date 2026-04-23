import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent, MouseEvent } from 'react'

type ServiceDefinition = {
  id: string
  name: string
  path: string
  command: string
  args?: string | null
  env: Record<string, string>
  createdAt: string
  updatedAt: string
}

type RuntimeStatus = 'Down' | 'Starting' | 'Up' | 'Stopping' | 'Error'

type ServiceRuntime = {
  serviceId: string
  status: RuntimeStatus
  pid?: number | null
  startedAt?: string | null
  lastExitCode?: number | null
  lastError?: string | null
}

type RuntimeActionResponse = {
  success: boolean
  runtime: ServiceRuntime
  code?: string | null
  message?: string | null
}

type BulkRuntimeActionResponse = {
  results: RuntimeActionResponse[]
}

type ServiceLogEntry = {
  sequence: number
  serviceId: string
  timestamp: string
  stream: string
  message: string
}

type ServiceLogListResponse = {
  serviceId: string
  logs: ServiceLogEntry[]
}

type StreamEvent<TPayload = unknown> = {
  type: 'log' | 'runtime'
  timestamp: string
  serviceId: string
  payload: TPayload
}

type FormState = {
  id?: string
  name: string
  path: string
  command: string
  args: string
}

const defaultForm: FormState = { name: '', path: '', command: '', args: '' }

async function apiFetch<T>(input: string, init?: RequestInit): Promise<T> {
  const response = await fetch(input, {
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
    ...init,
  })

  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`
    try {
      const error = (await response.json()) as { message?: string }
      if (error?.message) {
        message = error.message
      }
    } catch {
      // Ignore non-json error response.
    }
    throw new Error(message)
  }

  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

function App() {
  const [services, setServices] = useState<ServiceDefinition[]>([])
  const [runtimeMap, setRuntimeMap] = useState<Record<string, ServiceRuntime>>({})
  const [selectedServiceId, setSelectedServiceId] = useState<string | null>(null)
  const [logsByService, setLogsByService] = useState<Record<string, ServiceLogEntry[]>>({})
  const [isLoading, setIsLoading] = useState(true)
  const [isSaving, setIsSaving] = useState(false)
  const [showForm, setShowForm] = useState(false)
  const [form, setForm] = useState<FormState>(defaultForm)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [autoScroll, setAutoScroll] = useState(true)
  const logsRef = useRef<HTMLDivElement | null>(null)

  const selectedService = useMemo(
    () => services.find((item) => item.id === selectedServiceId) ?? null,
    [services, selectedServiceId],
  )

  const selectedLogs = useMemo(
    () => (selectedServiceId ? (logsByService[selectedServiceId] ?? []) : []),
    [logsByService, selectedServiceId],
  )

  useEffect(() => {
    async function initialLoad() {
      setIsLoading(true)
      setError(null)
      try {
        const [serviceList, runtimeList] = await Promise.all([
          apiFetch<ServiceDefinition[]>('/api/services'),
          apiFetch<ServiceRuntime[]>('/api/services/runtime'),
        ])
        setServices(serviceList)
        setRuntimeMap(
          runtimeList.reduce<Record<string, ServiceRuntime>>((acc, item) => {
            acc[item.serviceId] = item
            return acc
          }, {}),
        )
        setSelectedServiceId((current) => current ?? serviceList[0]?.id ?? null)
      } catch (loadError) {
        setError((loadError as Error).message)
      } finally {
        setIsLoading(false)
      }
    }
    void initialLoad()
  }, [])

  useEffect(() => {
    if (!selectedServiceId) {
      return
    }
    const serviceId = selectedServiceId
    async function fetchInitialLogs() {
      try {
        const payload = await apiFetch<ServiceLogListResponse>(`/api/services/${serviceId}/logs?take=300`)
        setLogsByService((current) => ({ ...current, [serviceId]: payload.logs }))
      } catch (loadError) {
        setError((loadError as Error).message)
      }
    }
    void fetchInitialLogs()
  }, [selectedServiceId])

  useEffect(() => {
    const eventSource = new EventSource('/api/stream')

    eventSource.addEventListener('runtime', (event) => {
      const data = JSON.parse((event as MessageEvent).data) as StreamEvent<{
        status: RuntimeStatus
        message?: string | null
        exitCode?: number | null
      }>

      setRuntimeMap((current) => {
        const existing = current[data.serviceId]
        return {
          ...current,
          [data.serviceId]: {
            serviceId: data.serviceId,
            status: data.payload.status,
            pid: existing?.pid ?? null,
            startedAt: existing?.startedAt ?? null,
            lastExitCode: data.payload.exitCode ?? existing?.lastExitCode ?? null,
            lastError: data.payload.message ?? existing?.lastError ?? null,
          },
        }
      })
    })

    eventSource.addEventListener('log', (event) => {
      const data = JSON.parse((event as MessageEvent).data) as StreamEvent<ServiceLogEntry>
      const logEntry = data.payload
      setLogsByService((current) => {
        const existing = current[logEntry.serviceId] ?? []
        if (existing.some((item) => item.sequence === logEntry.sequence)) {
          return current
        }
        const updated = [...existing, logEntry].slice(-1000)
        return { ...current, [logEntry.serviceId]: updated }
      })
    })

    eventSource.onerror = () => {
      setNotice('Realtime stream disconnected, retrying...')
    }

    return () => {
      eventSource.close()
    }
  }, [])

  useEffect(() => {
    if (!autoScroll || !logsRef.current) {
      return
    }
    logsRef.current.scrollTop = logsRef.current.scrollHeight
  }, [selectedLogs, autoScroll])

  async function loadAll() {
    setIsLoading(true)
    setError(null)
    try {
      const [serviceList, runtimeList] = await Promise.all([
        apiFetch<ServiceDefinition[]>('/api/services'),
        apiFetch<ServiceRuntime[]>('/api/services/runtime'),
      ])
      setServices(serviceList)
      setRuntimeMap(
        runtimeList.reduce<Record<string, ServiceRuntime>>((acc, item) => {
          acc[item.serviceId] = item
          return acc
        }, {}),
      )
      if (!selectedServiceId && serviceList.length > 0) {
        setSelectedServiceId(serviceList[0].id)
      }
    } catch (loadError) {
      setError((loadError as Error).message)
    } finally {
      setIsLoading(false)
    }
  }

  async function loadLogs(serviceId: string) {
    try {
      const payload = await apiFetch<ServiceLogListResponse>(`/api/services/${serviceId}/logs?take=300`)
      setLogsByService((current) => ({ ...current, [serviceId]: payload.logs }))
    } catch (loadError) {
      setError((loadError as Error).message)
    }
  }

  async function runLifecycle(serviceId: string, action: 'start' | 'stop' | 'restart') {
    setError(null)
    try {
      const result = await apiFetch<RuntimeActionResponse>(`/api/services/${serviceId}/${action}`, {
        method: 'POST',
      })
      setRuntimeMap((current) => ({ ...current, [serviceId]: result.runtime }))
      setNotice(`Service ${action} completed`)
      if (selectedServiceId === serviceId) {
        await loadLogs(serviceId)
      }
    } catch (actionError) {
      setError((actionError as Error).message)
    }
  }

  async function runBulk(action: 'start-all' | 'stop-all') {
    setError(null)
    try {
      const payload = await apiFetch<BulkRuntimeActionResponse>(`/api/services/${action}`, { method: 'POST' })
      setRuntimeMap((current) => {
        const next = { ...current }
        for (const result of payload.results) {
          next[result.runtime.serviceId] = result.runtime
        }
        return next
      })
      setNotice(`${action} completed (${payload.results.length} services)`)
    } catch (actionError) {
      setError((actionError as Error).message)
    }
  }

  function openCreateForm() {
    setForm(defaultForm)
    setShowForm(true)
  }

  function openEditForm(item: ServiceDefinition) {
    setForm({
      id: item.id,
      name: item.name,
      path: item.path,
      command: item.command,
      args: item.args ?? '',
    })
    setShowForm(true)
  }

  async function saveForm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setIsSaving(true)
    setError(null)
    try {
      const payload = {
        name: form.name,
        path: form.path,
        command: form.command,
        args: form.args || null,
        env: {},
      }

      if (form.id) {
        await apiFetch<ServiceDefinition>(`/api/services/${form.id}`, {
          method: 'PUT',
          body: JSON.stringify(payload),
        })
        setNotice(`Service ${form.name} updated`)
      } else {
        await apiFetch<ServiceDefinition>('/api/services', {
          method: 'POST',
          body: JSON.stringify(payload),
        })
        setNotice(`Service ${form.name} created`)
      }
      setShowForm(false)
      await loadAll()
    } catch (saveError) {
      setError((saveError as Error).message)
    } finally {
      setIsSaving(false)
    }
  }

  async function removeService(item: ServiceDefinition) {
    const confirmed = window.confirm(`Delete service "${item.name}"?`)
    if (!confirmed) {
      return
    }
    setError(null)
    try {
      await apiFetch<void>(`/api/services/${item.id}`, { method: 'DELETE' })
      setNotice(`Service ${item.name} deleted`)
      if (selectedServiceId === item.id) {
        setSelectedServiceId(null)
      }
      await loadAll()
    } catch (removeError) {
      setError((removeError as Error).message)
    }
  }

  return (
    <div className="page">
      <header className="header">
        <h1>SpinUp</h1>
        <div className="actions">
          <button onClick={openCreateForm}>Add Service</button>
          <button onClick={() => void runBulk('start-all')}>Start All</button>
          <button onClick={() => void runBulk('stop-all')}>Stop All</button>
          <button onClick={() => void loadAll()}>Refresh</button>
        </div>
      </header>

      {error ? <div className="banner error">{error}</div> : null}
      {notice ? <div className="banner notice">{notice}</div> : null}

      <main className="layout">
        <section className="services">
          <h2>Services</h2>
          {isLoading ? <p>Loading services...</p> : null}
          {!isLoading && services.length === 0 ? <p>No services configured yet.</p> : null}
          <div className="service-list">
            {services.map((service) => {
              const runtime = runtimeMap[service.id]
              const status = runtime?.status ?? 'Down'
              return (
                <article
                  key={service.id}
                  className={`service-card ${selectedServiceId === service.id ? 'selected' : ''}`}
                  onClick={() => setSelectedServiceId(service.id)}
                >
                  <div className="service-title-row">
                    <h3>{service.name}</h3>
                    <span className={`status ${status.toLowerCase()}`}>{status}</span>
                  </div>
                  <p className="muted">{service.path}</p>
                  <p className="mono">
                    {service.command}
                    {service.args ? ` ${service.args}` : ''}
                  </p>
                  <div className="row">
                    <small>PID: {runtime?.pid ?? '-'}</small>
                    <small>Exit: {runtime?.lastExitCode ?? '-'}</small>
                  </div>
                  <div className="actions row">
                    <button onClick={(e) => handleCardAction(e, () => void runLifecycle(service.id, 'start'))}>Start</button>
                    <button onClick={(e) => handleCardAction(e, () => void runLifecycle(service.id, 'stop'))}>Stop</button>
                    <button onClick={(e) => handleCardAction(e, () => void runLifecycle(service.id, 'restart'))}>Restart</button>
                    <button onClick={(e) => handleCardAction(e, () => openEditForm(service))}>Edit</button>
                    <button
                      className="danger"
                      onClick={(e) => handleCardAction(e, () => void removeService(service))}
                    >
                      Delete
                    </button>
                  </div>
                  {runtime?.lastError ? <p className="error-text">{runtime.lastError}</p> : null}
                </article>
              )
            })}
          </div>
        </section>

        <section className="logs">
          <div className="logs-header">
            <h2>Logs {selectedService ? `- ${selectedService.name}` : ''}</h2>
            <label>
              <input type="checkbox" checked={autoScroll} onChange={(e) => setAutoScroll(e.target.checked)} />
              Auto-scroll
            </label>
          </div>
          <div className="log-window" ref={logsRef}>
            {selectedServiceId === null ? <p>Select a service to view logs.</p> : null}
            {selectedServiceId !== null && selectedLogs.length === 0 ? <p>No logs yet.</p> : null}
            {selectedLogs.map((entry) => (
              <pre key={`${entry.serviceId}-${entry.sequence}`} className={`log-line ${entry.stream}`}>
                [{new Date(entry.timestamp).toLocaleTimeString()}] [{entry.stream}] {entry.message}
              </pre>
            ))}
          </div>
        </section>
      </main>

      {showForm ? (
        <div className="modal-backdrop">
          <div className="modal">
            <h2>{form.id ? 'Edit Service' : 'Add Service'}</h2>
            <form onSubmit={saveForm}>
              <label>
                Name
                <input
                  value={form.name}
                  required
                  onChange={(e) => setForm((current) => ({ ...current, name: e.target.value }))}
                />
              </label>
              <label>
                Path
                <input
                  value={form.path}
                  required
                  onChange={(e) => setForm((current) => ({ ...current, path: e.target.value }))}
                />
              </label>
              <label>
                Command
                <input
                  value={form.command}
                  required
                  onChange={(e) => setForm((current) => ({ ...current, command: e.target.value }))}
                />
              </label>
              <label>
                Args
                <input value={form.args} onChange={(e) => setForm((current) => ({ ...current, args: e.target.value }))} />
              </label>
              <div className="actions">
                <button type="submit" disabled={isSaving}>
                  {isSaving ? 'Saving...' : 'Save'}
                </button>
                <button type="button" onClick={() => setShowForm(false)}>
                  Cancel
                </button>
              </div>
            </form>
          </div>
        </div>
      ) : null}
    </div>
  )
}

export default App

function handleCardAction(event: MouseEvent, fn: () => void) {
  event.stopPropagation()
  fn()
}
