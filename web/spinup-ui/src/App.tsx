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
  status: RuntimeStatus | number
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

type FormState = {
  id?: string
  name: string
  path: string
  command: string
  args: string
  env: Record<string, string>
  healthCheckUrl: string
  startupTimeoutSeconds: string
}

const healthCheckUrlEnvKey = 'SPINUP_HEALTHCHECK_URL'
const startupTimeoutEnvKey = 'SPINUP_STARTUP_TIMEOUT_SECONDS'
const defaultForm: FormState = {
  name: '',
  path: '',
  command: '',
  args: '',
  env: {},
  healthCheckUrl: '',
  startupTimeoutSeconds: '',
}
const runtimeStatusByValue: RuntimeStatus[] = ['Down', 'Starting', 'Up', 'Stopping', 'Error']

function normalizeRuntimeStatus(status: RuntimeStatus | number | null | undefined): RuntimeStatus {
  if (typeof status === 'string') {
    return runtimeStatusByValue.includes(status as RuntimeStatus) ? (status as RuntimeStatus) : 'Down'
  }

  if (typeof status === 'number') {
    return runtimeStatusByValue[status] ?? 'Down'
  }

  return 'Down'
}

function getProp<T>(value: Record<string, unknown>, camelKey: string, pascalKey: string): T | undefined {
  return (value[camelKey] ?? value[pascalKey]) as T | undefined
}

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
  const [openCardMenuId, setOpenCardMenuId] = useState<string | null>(null)
  const [newEnvKey, setNewEnvKey] = useState('')
  const [newEnvValue, setNewEnvValue] = useState('')
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
      const raw = JSON.parse((event as MessageEvent).data) as Record<string, unknown>
      const serviceId = getProp<string>(raw, 'serviceId', 'ServiceId')
      const payload = getProp<Record<string, unknown>>(raw, 'payload', 'Payload')
      if (!serviceId || !payload) {
        return
      }

      const status = normalizeRuntimeStatus(getProp<RuntimeStatus | number>(payload, 'status', 'Status'))
      const message = getProp<string | null>(payload, 'message', 'Message') ?? null
      const exitCode = getProp<number | null>(payload, 'exitCode', 'ExitCode') ?? null
      const nextPid = getProp<number | null>(payload, 'pid', 'Pid')
      const nextStartedAt = getProp<string | null>(payload, 'startedAt', 'StartedAt')

      setRuntimeMap((current) => {
        const existing = current[serviceId]
        return {
          ...current,
          [serviceId]: {
            serviceId,
            status,
            pid: nextPid !== undefined ? nextPid : (existing?.pid ?? null),
            startedAt: nextStartedAt !== undefined ? nextStartedAt : (existing?.startedAt ?? null),
            lastExitCode: exitCode ?? existing?.lastExitCode ?? null,
            lastError: message ?? existing?.lastError ?? null,
          },
        }
      })
    })

    eventSource.addEventListener('log', (event) => {
      const raw = JSON.parse((event as MessageEvent).data) as Record<string, unknown>
      const payload = getProp<Record<string, unknown>>(raw, 'payload', 'Payload')
      if (!payload) {
        return
      }

      const serviceId = getProp<string>(payload, 'serviceId', 'ServiceId')
      const sequence = getProp<number>(payload, 'sequence', 'Sequence')
      const timestamp = getProp<string>(payload, 'timestamp', 'Timestamp')
      const stream = getProp<string>(payload, 'stream', 'Stream')
      const message = getProp<string>(payload, 'message', 'Message')
      if (!serviceId || sequence === undefined || !timestamp || !stream || message === undefined) {
        return
      }

      const logEntry: ServiceLogEntry = { serviceId, sequence, timestamp, stream, message }
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

  useEffect(() => {
    if (!notice) {
      return
    }

    const timer = window.setTimeout(() => setNotice(null), 2500)
    return () => window.clearTimeout(timer)
  }, [notice])

  async function loadAll(options?: { silent?: boolean }) {
    if (!options?.silent) {
      setIsLoading(true)
    }
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
      if (!options?.silent) {
        setIsLoading(false)
      }
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
    setNewEnvKey('')
    setNewEnvValue('')
    setShowForm(true)
  }

  function openEditForm(item: ServiceDefinition) {
    setForm({
      id: item.id,
      name: item.name,
      path: item.path,
      command: item.command,
      args: item.args ?? '',
      env: item.env ?? {},
      healthCheckUrl: item.env?.[healthCheckUrlEnvKey] ?? '',
      startupTimeoutSeconds: item.env?.[startupTimeoutEnvKey] ?? '',
    })
    setNewEnvKey('')
    setNewEnvValue('')
    setShowForm(true)
  }

  function addEnvVar() {
    const key = newEnvKey.trim()
    if (!key) {
      setError('Environment variable key is required.')
      return
    }

    if (key === healthCheckUrlEnvKey) {
      setForm((current) => ({ ...current, healthCheckUrl: newEnvValue.trim() }))
    } else {
      setForm((current) => ({
        ...current,
        env: {
          ...current.env,
          [key]: newEnvValue,
        },
      }))
    }

    setError(null)
    setNewEnvKey('')
    setNewEnvValue('')
  }

  function removeEnvVar(key: string) {
    setForm((current) => {
      const nextEnv = { ...current.env }
      delete nextEnv[key]
      return { ...current, env: nextEnv }
    })
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
        env: (() => {
          const nextEnv = { ...form.env }
          if (form.healthCheckUrl.trim()) {
            nextEnv[healthCheckUrlEnvKey] = form.healthCheckUrl.trim()
          } else {
            delete nextEnv[healthCheckUrlEnvKey]
          }

          if (form.startupTimeoutSeconds.trim()) {
            nextEnv[startupTimeoutEnvKey] = form.startupTimeoutSeconds.trim()
          } else {
            delete nextEnv[startupTimeoutEnvKey]
          }

          return nextEnv
        })(),
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
      // Refresh service cards in the background so the modal is immediately usable again.
      void loadAll({ silent: true })
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
      await loadAll({ silent: true })
    } catch (removeError) {
      setError((removeError as Error).message)
    }
  }

  function clearSelectedLogs() {
    if (!selectedServiceId) {
      return
    }
    const serviceId = selectedServiceId
    setError(null)
    void (async () => {
      try {
        await apiFetch<void>(`/api/services/${serviceId}/logs`, { method: 'DELETE' })
        setLogsByService((current) => ({ ...current, [serviceId]: [] }))
        setNotice('Console cleared')
      } catch (clearError) {
        setError((clearError as Error).message)
      }
    })()
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

      <main className="layout">
        <section className="services">
          <h2>Services</h2>
          {isLoading ? <p>Loading services...</p> : null}
          {!isLoading && services.length === 0 ? <p>No services configured yet.</p> : null}
          <div className="service-list">
            {services.map((service) => {
              const runtime = runtimeMap[service.id]
              const status = normalizeRuntimeStatus(runtime?.status)
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
                    <div className="more-actions">
                      <button
                        className="icon-button"
                        aria-label={`More actions for ${service.name}`}
                        onClick={(e) =>
                          handleCardAction(e, () =>
                            setOpenCardMenuId((current) => (current === service.id ? null : service.id)),
                          )
                        }
                      >
                        ...
                      </button>
                      {openCardMenuId === service.id ? (
                        <div className="more-actions-menu">
                          <button
                            onClick={(e) =>
                              handleCardAction(e, () => {
                                setOpenCardMenuId(null)
                                openEditForm(service)
                              })
                            }
                          >
                            Edit
                          </button>
                          <button
                            className="danger"
                            onClick={(e) =>
                              handleCardAction(e, () => {
                                setOpenCardMenuId(null)
                                void removeService(service)
                              })
                            }
                          >
                            Delete
                          </button>
                        </div>
                      ) : null}
                    </div>
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
            <div className="logs-controls">
              <button type="button" onClick={clearSelectedLogs} disabled={selectedServiceId === null}>
                Clear Console
              </button>
              <label>
                <input type="checkbox" checked={autoScroll} onChange={(e) => setAutoScroll(e.target.checked)} />
                Auto-scroll
              </label>
            </div>
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
              <label>
                Health Check URL
                <input
                  value={form.healthCheckUrl}
                  placeholder="http://localhost:5000/health"
                  onChange={(e) => setForm((current) => ({ ...current, healthCheckUrl: e.target.value }))}
                />
              </label>
              <label>
                Startup Timeout (seconds)
                <input
                  value={form.startupTimeoutSeconds}
                  placeholder="30"
                  onChange={(e) => setForm((current) => ({ ...current, startupTimeoutSeconds: e.target.value }))}
                />
              </label>
              <div className="env-editor">
                <p className="env-editor-title">Environment Variables</p>
                <div className="env-add-row">
                  <input
                    value={newEnvKey}
                    placeholder="Key (example: JWT_SECRET)"
                    onChange={(e) => setNewEnvKey(e.target.value)}
                  />
                  <input
                    value={newEnvValue}
                    placeholder="Value"
                    onChange={(e) => setNewEnvValue(e.target.value)}
                  />
                  <button type="button" onClick={addEnvVar}>
                    Add
                  </button>
                </div>
                <div className="env-list">
                  {Object.entries(form.env)
                    .filter(([key]) => key !== healthCheckUrlEnvKey && key !== startupTimeoutEnvKey)
                    .map(([key, value]) => (
                      <div key={key} className="env-row">
                        <input value={key} readOnly />
                        <input
                          value={value}
                          onChange={(e) =>
                            setForm((current) => ({
                              ...current,
                              env: {
                                ...current.env,
                                [key]: e.target.value,
                              },
                            }))
                          }
                        />
                        <button type="button" className="danger" onClick={() => removeEnvVar(key)}>
                          Remove
                        </button>
                      </div>
                    ))}
                </div>
              </div>
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

      {notice ? <div className="toast notice-toast">{notice}</div> : null}
    </div>
  )
}

export default App

function handleCardAction(event: MouseEvent, fn: () => void) {
  event.stopPropagation()
  fn()
}
