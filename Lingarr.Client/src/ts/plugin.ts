import { LabelValue } from '@/ts'

export const PLUGIN_SETTING_TYPE = {
    TEXT: 'Text',
    URL: 'Url',
    SECRET: 'Secret',
    REMOTE_DROPDOWN: 'RemoteDropdown',
    OAUTH: 'OAuth',
    TOGGLE: 'Toggle',
    DROPDOWN: 'Dropdown'
} as const

export type PluginSettingType = (typeof PLUGIN_SETTING_TYPE)[keyof typeof PLUGIN_SETTING_TYPE]

export interface IPluginSettingField {
    key: string
    label: string
    type: PluginSettingType
    required: boolean
    default?: string | null
    description?: string | null
    optionsEndpoint?: string | null
    options?: { value: string; label: string }[] | null
    minLength?: number | null
    validationErrorMessage?: string | null
}

export interface IPluginSummary {
    provider: string
    displayName: string
    description?: string | null
    isBuiltIn: boolean
    sourceFile?: string | null
    hasRequestTemplate: boolean
    supportsInstructionProfiles: boolean
    enabled?: boolean
    order?: number
    failurePolicy?: string
    capabilities?: string[]
    panels?: { id: string }[]
}

export interface IPluginUiTab {
    section: string
    tabId: string
    label: string
    provider: string
}

export interface IPluginUiPanel {
    provider: string
    id: string
    section: string
    tabId: string
    tabLabel: string
    title: string
    description?: string | null
    fields: IPluginSettingField[]
    actions: { id: string; label: string }[]
    enabled?: boolean
    order?: number
    failurePolicy?: string
}

export interface IPluginUi {
    tabs: IPluginUiTab[]
    panels: IPluginUiPanel[]
}

export interface IPluginManifest extends IPluginSummary {
    settings: IPluginSettingField[]
}

export interface IPluginStatus {
    provider: string
    configured: boolean
    missingFields: string[]
}

export interface IPluginOptionsResponse {
    options?: LabelValue[] | null
    message?: string | null
}

export interface IXaiOAuthStatus {
    connected: boolean
    expiresAt?: string | null
    label: string
}

export interface IXaiOAuthDevice {
    flowId: string
    userCode: string
    verificationUri: string
    verificationUriComplete?: string | null
    intervalSeconds: number
    expiresAt: string
}

export interface IXaiOAuthPoll {
    status: 'pending' | 'connected' | 'expired' | 'denied' | 'error'
    intervalSeconds?: number | null
    message?: string | null
}
