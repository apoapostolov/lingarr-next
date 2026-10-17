export interface IPlexStatus {
    connected: boolean
    username?: string | null
    serverName?: string | null
    serverUrl?: string | null
    authMethod?: string | null
    source: string
    setSelectedSubtitle: boolean
    defaultSubtitleLanguage?: string | null
    needsServer: boolean
}

export interface IPlexPin {
    pinId: number
    code: string
    authUrl: string
}

export interface IPlexPoll {
    status: 'pending' | 'connected' | 'expired' | 'invalid'
    message?: string | null
    username?: string | null
}

export interface IPlexConnection {
    uri: string
    local: boolean
    relay: boolean
    latencyMs: number
}

export interface IPlexServer {
    name: string
    machineIdentifier: string
    connections: IPlexConnection[]
}

export interface IPlexTest {
    ok: boolean
    message?: string | null
}

export interface IPlexLibrary {
    key: string
    type: string
    title: string
    scheduled: boolean
}

export interface IPlexLibraries {
    libraries: IPlexLibrary[]
    running: boolean
}
