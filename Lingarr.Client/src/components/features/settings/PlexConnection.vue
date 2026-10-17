<template>
    <CardComponent title="Plex">
        <template #description>
            Connect Plex to attach translated subtitles to matched movies and episodes.
        </template>
        <template #content>
            <p v-if="status?.source === 'environment'" class="text-secondary-content text-sm">
                Using the Plex token configured on the server. Sign out to change accounts.
            </p>

            <div v-if="pin" class="space-y-2">
                <p class="text-primary-content text-sm">
                    Authorize Lingarr in Plex, then return and enter this code:
                    <span class="font-mono">{{ pin.code }}</span>
                </p>
                <a
                    :href="pin.authUrl"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="text-accent block text-sm underline break-all">
                    {{ pin.authUrl }}
                </a>
            </div>

            <div v-if="status?.connected && !pin" class="space-y-1 text-sm">
                <p v-if="status.username" class="text-primary-content">
                    Signed in as {{ status.username }}
                </p>
                <p v-if="status.serverName" class="text-secondary-content">
                    Server: {{ status.serverName }}
                </p>
                <p v-if="status.needsServer" class="text-secondary-content">
                    Plex account connected, but Lingarr cannot reach its server.
                </p>
            </div>

            <div v-if="servers.length > 1" class="space-y-2">
                <p class="text-primary-content text-sm">Select a Plex server reachable from Lingarr.</p>
                <button
                    v-for="server in servers"
                    :key="server.machineIdentifier"
                    type="button"
                    class="border-accent/40 hover:bg-accent/10 block w-full rounded-md border px-3 py-2 text-left text-sm"
                    @click="chooseServer(server)">
                    <span class="text-primary-content font-medium">{{ server.name }}</span>
                    <span class="text-secondary-content block">{{ server.connections[0]?.uri }}</span>
                </button>
            </div>

            <div v-if="showTokenForm" class="space-y-2">
                <InputComponent
                    v-model="manualUrl"
                    :validation-type="INPUT_VALIDATION_TYPE.URL"
                    label="Server address"
                    error-message="Enter the Plex address, for example http://plex:32400"
                    @update:validation="(value) => (urlValid = value)" />
                <InputComponent
                    v-model="manualToken"
                    :validation-type="INPUT_VALIDATION_TYPE.STRING"
                    :type="INPUT_TYPE.PASSWORD"
                    label="Token"
                    error-message="Enter the Plex token"
                    @update:validation="(value) => (tokenValid = value)" />
            </div>

            <p v-if="error" class="text-error text-sm" role="alert">{{ error }}</p>
            <p v-if="notice" class="text-secondary-content text-sm" role="status">{{ notice }}</p>

            <div class="flex flex-wrap gap-2">
                <ButtonComponent
                    v-if="!status?.connected"
                    size="sm"
                    :disabled="busy"
                    @click="startPin">
                    Sign in with Plex
                </ButtonComponent>
                <ButtonComponent
                    v-if="!status?.connected || status.needsServer"
                    variant="secondary"
                    size="sm"
                    :disabled="busy"
                    @click="showTokenForm = !showTokenForm">
                    Use a token
                </ButtonComponent>
                <ButtonComponent
                    v-if="showTokenForm"
                    size="sm"
                    :disabled="busy || !urlValid || !tokenValid"
                    @click="saveToken">
                    Save token
                </ButtonComponent>
                <ButtonComponent
                    v-if="status?.connected && status.serverUrl"
                    variant="secondary"
                    size="sm"
                    :disabled="busy"
                    @click="testConnection">
                    Test
                </ButtonComponent>
                <ButtonComponent
                    v-if="pin"
                    variant="ghost"
                    size="sm"
                    @click="cancelPin">
                    Cancel
                </ButtonComponent>
                <ButtonComponent
                    v-if="status?.connected"
                    variant="ghost"
                    size="sm"
                    :disabled="busy"
                    @click="logout">
                    Sign out
                </ButtonComponent>
            </div>

            <SelectComponent
                label="Subtitle language for selection"
                :options="languageOptions"
                :selected="language"
                placeholder="Choose a language"
                @update:selected="saveLanguage" />
            <ToggleButton
                :model-value="selectSubtitle"
                aria-label="Select the translated subtitle in Plex"
                @update:model-value="saveSelection">
                <span class="text-primary-content text-sm font-medium">
                    After translation, select this subtitle in Plex
                </span>
            </ToggleButton>
            <p class="text-secondary-content text-sm">
                Lingarr uploads the sidecar if Plex does not detect it after refresh.
            </p>

            <div v-if="status?.connected && status.serverUrl && !pin" class="space-y-3">
                <p class="text-primary-content text-sm font-medium">Libraries</p>
                <p class="text-secondary-content text-sm">
                    Refresh a library now, or include it in the schedule. Lingarr refreshes
                    one enabled library at a time and waits 15 minutes before the next.
                    After each scan it selects the configured subtitle when that sidecar
                    is on the item.
                </p>
                <p v-if="librariesError" class="text-error text-sm" role="alert">{{ librariesError }}</p>
                <p
                    v-else-if="librariesLoaded && libraries.length === 0"
                    class="text-secondary-content text-sm">
                    Plex returned no libraries.
                </p>
                <div
                    v-for="library in libraries"
                    :key="library.key"
                    class="flex items-center justify-between gap-3">
                    <div class="min-w-0">
                        <p class="text-primary-content truncate text-sm font-medium">{{ library.title }}</p>
                        <p class="text-secondary-content text-xs">{{ libraryTypeLabel(library.type) }}</p>
                    </div>
                    <div class="flex shrink-0 items-center gap-2">
                        <button
                            type="button"
                            class="text-accent hover:bg-accent/10 rounded-md p-1 disabled:cursor-not-allowed disabled:opacity-50"
                            :disabled="busy || refreshRunning || refreshingKey === library.key"
                            :aria-label="`Refresh ${library.title} now`"
                            @click="refreshLibrary(library)">
                            <ReloadIcon
                                class="h-5 w-5"
                                :class="{ 'animate-spin': refreshingKey === library.key }" />
                        </button>
                        <ToggleButton
                            :model-value="library.scheduled"
                            :aria-label="`Include ${library.title} in the scheduled Plex refresh`"
                            @update:model-value="(value) => saveLibrarySchedule(library, value)" />
                    </div>
                </div>
                <InputComponent
                    v-model="refreshSchedule"
                    label="Cron expression (UTC)"
                    :placeholder="'0 4 * * 0'"
                    :validation-type="INPUT_VALIDATION_TYPE.CRON"
                    @update:validation="(value) => (refreshScheduleValid = value)" />
            </div>

            <ToggleButton
                :model-value="translateMoviesOnAdd"
                aria-label="Translate when Plex adds a movie"
                @update:model-value="saveTranslateMoviesOnAdd">
                <span class="text-primary-content text-sm font-medium">
                    Translate when Plex adds a movie
                </span>
            </ToggleButton>
            <ToggleButton
                :model-value="translateEpisodesOnAdd"
                aria-label="Translate when Plex adds an episode"
                @update:model-value="saveTranslateEpisodesOnAdd">
                <span class="text-primary-content text-sm font-medium">
                    Translate when Plex adds an episode
                </span>
            </ToggleButton>
            <div class="space-y-2">
                <p class="text-secondary-content text-sm">Add this URL under Plex Settings → Webhooks.</p>
                <p class="text-secondary-content text-sm">
                    Translation queues when source subtitles exist and the target is missing.
                    Extract embedded tracks before sending webhooks.
                </p>
                <CodeSnippet class="block overflow-x-auto">{{ plexWebhookUrl }}</CodeSnippet>
                <p v-if="authEnabled === 'true'" class="text-secondary-content text-sm">
                    Plex webhooks cannot send Lingarr credentials. Disable API authentication for this endpoint.
                </p>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import services from '@/services'
import { INPUT_TYPE, INPUT_VALIDATION_TYPE, IPlexLibrary, IPlexPin, IPlexServer, IPlexStatus, SETTINGS } from '@/ts'
import { useSettingStore } from '@/store/setting'
import { useTranslateStore } from '@/store/translate'
import { resolveUrl } from '@/utils/baseUrl'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import CardComponent from '@/components/common/CardComponent.vue'
import CodeSnippet from '@/components/common/CodeSnippet.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import ReloadIcon from '@/components/icons/ReloadIcon.vue'

const plexWebhookUrl = resolveUrl('/api/webhook/plex')

const settingsStore = useSettingStore()
const translateStore = useTranslateStore()
const status = ref<IPlexStatus | null>(null)
const pin = ref<IPlexPin | null>(null)
const servers = ref<IPlexServer[]>([])
const showTokenForm = ref(false)
const manualUrl = ref('')
const manualToken = ref('')
const urlValid = ref(false)
const tokenValid = ref(false)
const busy = ref(false)
const error = ref<string | null>(null)
const notice = ref<string | null>(null)
const libraries = ref<IPlexLibrary[]>([])
const librariesLoaded = ref(false)
const librariesError = ref<string | null>(null)
const refreshingKey = ref<string | null>(null)
const refreshRunning = ref(false)
const refreshScheduleValid = ref(false)
let pollTimer: ReturnType<typeof setTimeout> | null = null

const language = computed(
    () => (settingsStore.getSetting(SETTINGS.PLEX_DEFAULT_SUBTITLE_LANGUAGE) as string) || ''
)
const selectSubtitle = computed(
    () => (settingsStore.getSetting(SETTINGS.PLEX_SET_SELECTED_SUBTITLE) as string) || 'false'
)
const translateMoviesOnAdd = computed(
    () => (settingsStore.getSetting(SETTINGS.PLEX_TRANSLATE_MOVIES_ON_LIBRARY_NEW) as string) || 'true'
)
const translateEpisodesOnAdd = computed(
    () => (settingsStore.getSetting(SETTINGS.PLEX_TRANSLATE_EPISODES_ON_LIBRARY_NEW) as string) || 'true'
)
const authEnabled = computed(
    () => (settingsStore.getSetting(SETTINGS.AUTH_ENABLED) as string) || 'false'
)
const languageOptions = computed(() =>
    translateStore.getLanguages.map((item) => ({ value: item.code, label: item.name }))
)
const refreshSchedule = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.PLEX_LIBRARY_REFRESH_SCHEDULE) as string) || '0 4 * * 0',
    set: (value: string): void => {
        void saveRefreshSchedule(value)
    }
})

function clearPoll() {
    if (pollTimer) {
        clearTimeout(pollTimer)
        pollTimer = null
    }
}

function detail(response: { data?: unknown } | undefined, fallback: string) {
    const data = response?.data
    if (typeof data === 'string' && data.length > 0) {
        return data
    }
    if (data && typeof data === 'object' && 'detail' in data && typeof data.detail === 'string') {
        return data.detail
    }
    return fallback
}

async function refreshStatus() {
    status.value = await services.plex.status()
    if (status.value.serverUrl) {
        manualUrl.value = status.value.serverUrl
    }
    await loadLibraries()
}

function libraryTypeLabel(type: string) {
    switch (type.toLowerCase()) {
        case 'movie':
            return 'Movies'
        case 'show':
            return 'TV'
        default:
            return type
    }
}

async function loadLibraries() {
    if (!status.value?.connected || !status.value.serverUrl || pin.value) {
        libraries.value = []
        librariesLoaded.value = false
        refreshRunning.value = false
        return
    }

    librariesError.value = null
    try {
        const result = await services.plex.libraries()
        libraries.value = result.libraries
        refreshRunning.value = result.running
        librariesLoaded.value = true
    } catch (response: any) {
        libraries.value = []
        librariesLoaded.value = true
        librariesError.value = detail(response, 'Lingarr could not list Plex libraries.')
    }
}

async function refreshLibrary(library: IPlexLibrary) {
    error.value = null
    notice.value = null
    librariesError.value = null
    refreshingKey.value = library.key
    busy.value = true
    try {
        const result = await services.plex.refreshLibrary(library.key)
        notice.value = result.message || `Plex library refresh started for ${library.title}.`
        refreshRunning.value = true
    } catch (response: any) {
        error.value = detail(response, 'Lingarr could not start the Plex library refresh.')
    } finally {
        busy.value = false
        refreshingKey.value = null
    }
}

async function saveLibrarySchedule(library: IPlexLibrary, value: string | boolean) {
    const on = String(value) === 'true'
    library.scheduled = on
    const keys = libraries.value.filter((item) => item.scheduled).map((item) => item.key)
    const json = JSON.stringify(keys)
    error.value = null
    try {
        await services.setting.setSetting(SETTINGS.PLEX_LIBRARY_REFRESH_KEYS, json)
        settingsStore.storeSetting(SETTINGS.PLEX_LIBRARY_REFRESH_KEYS, json)
    } catch (response: any) {
        library.scheduled = !on
        error.value = detail(response, 'Could not save the Plex library schedule.')
    }
}

async function saveRefreshSchedule(value: string) {
    if (!refreshScheduleValid.value) {
        return
    }
    error.value = null
    try {
        await services.setting.setSetting(SETTINGS.PLEX_LIBRARY_REFRESH_SCHEDULE, value)
        settingsStore.storeSetting(SETTINGS.PLEX_LIBRARY_REFRESH_SCHEDULE, value)
    } catch (response: any) {
        error.value = detail(response, 'Could not save the Plex library schedule.')
    }
}

async function loadServers() {
    servers.value = await services.plex.servers()
    if (servers.value.length === 1 && servers.value[0].connections.length > 0) {
        await chooseServer(servers.value[0])
        servers.value = []
    }
}

async function chooseServer(server: IPlexServer) {
    const connection = server.connections[0]
    if (!connection) {
        return
    }
    busy.value = true
    error.value = null
    try {
        status.value = await services.plex.selectServer(
            server.machineIdentifier,
            server.name,
            connection.uri
        )
        servers.value = []
        await loadLibraries()
        notice.value = `Using ${server.name}.`
    } catch (response: any) {
        error.value = detail(response, 'Lingarr could not open that Plex server.')
    } finally {
        busy.value = false
    }
}

function schedulePoll() {
    clearPoll()
    pollTimer = setTimeout(async () => {
        if (!pin.value) {
            return
        }
        try {
            const result = await services.plex.pollPin(pin.value.pinId)
            if (result.status === 'connected') {
                pin.value = null
                await refreshStatus()
                if (status.value?.needsServer) {
                    await loadServers()
                }
                notice.value = 'Signed in with Plex.'
                busy.value = false
                return
            }
            if (result.status === 'pending') {
                schedulePoll()
                return
            }
            error.value = result.message || 'Plex sign-in did not finish.'
            pin.value = null
            busy.value = false
        } catch (response: any) {
            error.value = detail(response, 'Lingarr could not check the Plex sign-in.')
            busy.value = false
        }
    }, 2000)
}

async function startPin() {
    clearPoll()
    busy.value = true
    error.value = null
    notice.value = null
    try {
        pin.value = await services.plex.startPin()
        window.open(pin.value.authUrl, '_blank', 'noopener,noreferrer')
        schedulePoll()
    } catch (response: any) {
        error.value = detail(response, 'Lingarr could not start Plex sign-in.')
        busy.value = false
    }
}

function cancelPin() {
    clearPoll()
    pin.value = null
    busy.value = false
}

async function saveToken() {
    busy.value = true
    error.value = null
    notice.value = null
    try {
        status.value = await services.plex.saveToken(manualUrl.value, manualToken.value)
        manualToken.value = ''
        showTokenForm.value = false
        await loadLibraries()
        notice.value = 'Plex token saved.'
    } catch (response: any) {
        error.value = detail(response, 'Lingarr could not use that Plex token.')
    } finally {
        busy.value = false
    }
}

async function testConnection() {
    busy.value = true
    error.value = null
    notice.value = null
    try {
        const result = await services.plex.test()
        if (result.ok) {
            notice.value = result.message || 'Plex connection succeeded.'
        } else {
            error.value = result.message || 'Plex connection failed.'
        }
    } catch (response: any) {
        error.value = detail(response, 'Plex connection failed.')
    } finally {
        busy.value = false
    }
}

async function logout() {
    clearPoll()
    busy.value = true
    error.value = null
    notice.value = null
    try {
        await services.plex.logout()
        pin.value = null
        servers.value = []
        libraries.value = []
        librariesLoaded.value = false
        await refreshStatus()
        notice.value = 'Signed out of Plex.'
    } catch (response: any) {
        error.value = detail(response, 'Lingarr could not sign out of Plex.')
    } finally {
        busy.value = false
    }
}

async function saveLanguage(code: string) {
    error.value = null
    try {
        await services.setting.setSetting(SETTINGS.PLEX_DEFAULT_SUBTITLE_LANGUAGE, code)
        settingsStore.storeSetting(SETTINGS.PLEX_DEFAULT_SUBTITLE_LANGUAGE, code)
    } catch (response: any) {
        error.value = detail(response, 'Could not save the subtitle language.')
    }
}

async function saveTranslateMoviesOnAdd(value: string | boolean) {
    await savePlexWebhookSwitch(SETTINGS.PLEX_TRANSLATE_MOVIES_ON_LIBRARY_NEW, value)
}

async function saveTranslateEpisodesOnAdd(value: string | boolean) {
    await savePlexWebhookSwitch(SETTINGS.PLEX_TRANSLATE_EPISODES_ON_LIBRARY_NEW, value)
}

async function savePlexWebhookSwitch(key: typeof SETTINGS.PLEX_TRANSLATE_MOVIES_ON_LIBRARY_NEW | typeof SETTINGS.PLEX_TRANSLATE_EPISODES_ON_LIBRARY_NEW, value: string | boolean) {
    const next = String(value)
    error.value = null
    try {
        await services.setting.setSetting(key, next)
        settingsStore.storeSetting(key, next)
    } catch (response: any) {
        error.value = detail(response, 'Could not save the Plex webhook setting.')
    }
}

async function saveSelection(value: string | boolean) {
    const next = String(value)
    error.value = null
    if (next === 'true' && !language.value) {
        error.value = 'Select a subtitle language before enabling this setting.'
        return
    }
    try {
        await services.setting.setSetting(SETTINGS.PLEX_SET_SELECTED_SUBTITLE, next)
        settingsStore.storeSetting(SETTINGS.PLEX_SET_SELECTED_SUBTITLE, next)
    } catch (response: any) {
        error.value = detail(response, 'Could not save the Plex subtitle setting.')
    }
}

onMounted(async () => {
    try {
        await refreshStatus()
        if (translateStore.getLanguages.length === 0) {
            await translateStore.setLanguages()
        }
    } catch {
        error.value = 'Lingarr could not read the Plex connection.'
    }
})

onBeforeUnmount(() => {
    clearPoll()
})
</script>
