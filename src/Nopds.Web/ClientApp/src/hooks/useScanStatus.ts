import { useEffect, useState } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { api, getAccessToken, refreshAuth } from '../api/client'
import type { ScanStatus } from '../api/types'

/** Live scan progress per library via the /hubs/scan SignalR hub (admin only). */
export function useScanStatus() {
  const [status, setStatus] = useState<Record<number, ScanStatus>>({})
  const qc = useQueryClient()

  useEffect(() => {
    let stopped = false
    api<ScanStatus[]>('/admin/scan').then((list) => !stopped && setStatus(Object.fromEntries(list.map((s) => [s.libraryId, s])))).catch(() => {})

    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/scan', { accessTokenFactory: async () => getAccessToken() ?? (await refreshAuth())?.accessToken ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    const put = (s: ScanStatus) => setStatus((prev) => ({ ...prev, [s.libraryId]: s }))
    conn.on('progress', put)
    conn.on('completed', (s: ScanStatus) => {
      put(s)
      qc.invalidateQueries({ queryKey: ['admin', 'libraries'] })
      qc.invalidateQueries({ queryKey: ['stats'] })
      qc.invalidateQueries({ queryKey: ['books'] })
    })
    conn.start().catch(() => {})
    return () => {
      stopped = true
      conn.stop()
    }
  }, [qc])

  return status
}
