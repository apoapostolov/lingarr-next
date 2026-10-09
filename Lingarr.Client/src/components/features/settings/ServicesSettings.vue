<template>
    <div class="translation-setup-grid p-4">
        <LanguageSettings class="min-w-0" />
        <CardComponent title="Provider" class="min-w-0">
            <template #description>
                Reorder providers to set translation priority. Configure the selected provider below.
            </template>
            <template #content>
            <SaveNotification ref="saveNotification" />

            <div class="space-y-2">
                <ol id="translation-services" class="space-y-3">
                    <li
                        v-for="(entry, index) in chain"
                        :key="entry.id"
                        :id="`service-${entry.provider}`"
                        class="flex cursor-pointer gap-3 rounded-md border p-3"
                        :class="rowClass(entry, index)"
                        @click="selectRow(index)"
                        @dragover.prevent="onDragOver(index, $event)"
                        @drop.prevent="onDrop">
                        <button
                            type="button"
                            draggable="true"
                            class="text-primary-content/70 hover:text-primary-content focus-visible:ring-accent mt-2 flex h-8 w-6 shrink-0 cursor-grab items-center justify-center rounded focus-visible:ring-2 focus-visible:outline-none active:cursor-grabbing"
                            title="Drag to reorder"
                            aria-label="Drag to reorder"
                            @click.stop
                            @dragstart="onDragStart(index, $event)"
                            @dragend="onDragEnd"
                            @keydown="onHandleKeydown(index, $event)">
                            <svg viewBox="0 0 10 16" class="h-4 w-3" fill="currentColor" aria-hidden="true">
                                <circle cx="2" cy="2" r="1.2" />
                                <circle cx="8" cy="2" r="1.2" />
                                <circle cx="2" cy="8" r="1.2" />
                                <circle cx="8" cy="8" r="1.2" />
                                <circle cx="2" cy="14" r="1.2" />
                                <circle cx="8" cy="14" r="1.2" />
                            </svg>
                        </button>
                        <span
                            class="bg-accent/20 text-accent-content mt-2 flex h-8 w-8 shrink-0 items-center justify-center rounded-md text-sm font-semibold tabular-nums"
                            :title="index === 0 ? 'Primary' : `Fallback ${index}`"
                            :aria-label="index === 0 ? 'Primary' : `Fallback ${index}`">
                            {{ index + 1 }}
                        </span>

                        <div class="flex min-w-0 flex-1 flex-col gap-2">
                            <!-- Row 1: provider -->
                            <SelectComponent
                                :selected="entry.provider"
                                :options="providerOptions"
                                placeholder="Select provider..."
                                @update:selected="(value: string) => setProvider(index, value)" />

                        </div>

                        <div class="mt-2 flex h-6 w-6 shrink-0 items-center justify-center">
                            <button
                                v-if="index > 0"
                                type="button"
                                class="text-primary-content hover:text-primary-content/50 focus-visible:ring-accent cursor-pointer rounded p-1 transition-colors focus-visible:ring-2 focus-visible:outline-none"
                                title="Remove fallback"
                                aria-label="Remove fallback"
                                @click.stop="removeRow(index)">
                                <TrashIcon class="h-4 w-4" />
                            </button>
                        </div>
                    </li>
                    <li>
                        <ButtonComponent variant="ghost" size="xs" @click="addRow">
                            <PlusIcon class="mr-1 h-3 w-3" />
                            Add fallback service
                        </ButtonComponent>
                    </li>
                </ol>
            </div>

            <div class="border-accent/30 mt-4 space-y-3 border-t pt-4">
                <div class="text-sm">
                    <span class="text-secondary-content/60">Selected</span>
                    <span class="ml-1 font-semibold">{{ configuringLabel }}</span>
                    <span class="text-secondary-content/60 ml-1">
                        (row {{ configuringIndex + 1 }})
                    </span>
                </div>

                <div
                    v-if="supportsModel(chain[configuringIndex]?.provider)"
                    class="flex items-center gap-2">
                    <div class="min-w-0 flex-1">
                        <SelectComponent
                            :ref="(el) => setModelSelectRef(configuringIndex, el)"
                            :selected="chain[configuringIndex]?.model ?? ''"
                            :options="modelOptions[configuringIndex] || []"
                            :load-on-open="true"
                            :sort-options="false"
                            placeholder="Select model..."
                            :no-options="modelError[configuringIndex] || 'Loading models...'"
                            @update:selected="(value: string) => setModel(configuringIndex, value)"
                            @fetch-options="() => loadModels(configuringIndex, false)" />
                    </div>
                    <ButtonComponent
                        variant="ghost"
                        size="xs"
                        title="Refresh models"
                        @click="loadModels(configuringIndex, true)">
                        Refresh
                    </ButtonComponent>
                </div>

                <div
                    v-if="supportsInstructions(chain[configuringIndex]?.provider)"
                    class="grid gap-2 rounded-md border border-accent/20 bg-primary/35 p-2 sm:grid-cols-2">
                    <label>
                        <span class="mb-1 block text-xs font-semibold text-primary-content/65">
                            System prompt
                        </span>
                        <select
                            :value="chain[configuringIndex]?.systemPromptProfileId ?? ''"
                            class="w-full rounded-md border border-accent bg-secondary px-2 py-2 text-sm text-primary-content"
                            @change="
                                setPromptProfile(
                                    configuringIndex,
                                    'system',
                                    ($event.target as HTMLSelectElement).value
                                )
                            ">
                            <option value="">Default · {{ activeSystemProfileName }}</option>
                            <option
                                v-for="profile in systemProfiles"
                                :key="profile.id"
                                :value="profile.id">
                                {{ profile.name }} · v{{ profile.currentVersionNumber ?? 'draft' }}
                            </option>
                        </select>
                    </label>
                    <label>
                        <span class="mb-1 block text-xs font-semibold text-primary-content/65">
                            Context prompt
                        </span>
                        <select
                            :value="chain[configuringIndex]?.contextPromptProfileId ?? ''"
                            class="w-full rounded-md border border-accent bg-secondary px-2 py-2 text-sm text-primary-content"
                            @change="
                                setPromptProfile(
                                    configuringIndex,
                                    'context',
                                    ($event.target as HTMLSelectElement).value
                                )
                            ">
                            <option value="">Default · {{ activeContextProfileName }}</option>
                            <option
                                v-for="profile in contextProfiles"
                                :key="profile.id"
                                :value="profile.id">
                                {{ profile.name }} · v{{ profile.currentVersionNumber ?? 'draft' }}
                            </option>
                        </select>
                    </label>
                    <router-link
                        :to="{ name: 'translation-prompts-settings' }"
                        class="text-xs text-accent underline sm:col-span-2">
                        Manage prompt profiles
                    </router-link>
                </div>

                <DynamicPluginForm
                    v-if="
                        credentialsManifest &&
                        credentialsManifest.provider.toLowerCase() ===
                            chain[configuringIndex]?.provider?.toLowerCase()
                    "
                    :key="credentialsManifest.provider"
                    :manifest="credentialsManifest"
                    @save="saveNotification?.show()" />
                <p v-else-if="manifestError" class="text-sm text-red-500">{{ manifestError }}</p>

                <div v-if="configuringManifest?.hasRequestTemplate" class="flex flex-col gap-3">
                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">Customize request template and prompts</span>
                        Adjust the AI request body, system prompt and context for translations.
                    </div>
                    <ButtonComponent
                        variant="primary"
                        size="md"
                        @click="
                            router.push({
                                name: 'request-template-settings',
                                params: { service: chain[configuringIndex]?.provider }
                            })
                        ">
                        Open Request Settings
                        <ArrowRight class="mt-1 ml-1 h-4 w-4" />
                    </ButtonComponent>
                </div>
            </div>
            </template>
        </CardComponent>
        <ProviderHealthPanel class="translation-setup-span min-w-0" />
    </div>
</template>

<style>
.translation-setup-grid {
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    gap: 1rem;
    align-items: start;
}

@media (min-width: 640px) {
    .translation-setup-grid {
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
    }

    .translation-setup-grid > .translation-setup-span {
        grid-column: 1 / -1;
    }
}
</style>

<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSettingStore } from '@/store/setting'
import {
    IPluginManifest,
    IPluginSummary,
    IPromptProfile,
    PLUGIN_SETTING_TYPE,
    SETTINGS,
    SERVICE_TYPE,
    SelectComponentExpose
} from '@/ts'
import servicesApi from '@/services'
import CardComponent from '@/components/common/CardComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import DynamicPluginForm from '@/components/features/settings/DynamicPluginForm.vue'
import LanguageSettings from '@/components/features/settings/LanguageSettings.vue'
import ProviderHealthPanel from '@/components/features/providerHealth/ProviderHealthPanel.vue'
import ArrowRight from '@/components/icons/ArrowRight.vue'
import TrashIcon from '@/components/icons/TrashIcon.vue'
import PlusIcon from '@/components/icons/PlusIcon.vue'

export type ChainEntry = {
    id: string
    provider: string
    model?: string | null
    systemPromptProfileId?: number | null
    contextPromptProfileId?: number | null
}

function newRowId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return crypto.randomUUID()
    }
    return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}

const MODEL_PROVIDERS = new Set([
    'openai',
    'anthropic',
    'gemini',
    'deepseek',
    'localai',
    'openrouter',
    'zai',
    'opencode-go',
    'qwen',
    'qwen-mt',
    'xai',
    'xai-oauth',
    'mistral'
])

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()

const route = useRoute()
const router = useRouter()
const providerOptions = ref<{ value: string; label: string }[]>([])
const focusedProvider = ref('')
const chain = ref<ChainEntry[]>([])
const configuringIndex = ref(0)
const configuringManifest = ref<IPluginManifest | null>(null)
const manifestError = ref<string | null>(null)
let manifestRequest = 0
const promptProfiles = ref<IPromptProfile[]>([])
const instructionProviders = ref(new Set<string>())
const activeSystemProfileId = ref(0)
const activeContextProfileId = ref(0)
const modelOptions = reactive<Record<number, { value: string; label: string }[]>>({})
const modelError = reactive<Record<number, string | null>>({})
const modelSelectRefs = ref<Record<number, SelectComponentExpose | null>>({})

function setModelSelectRef(index: number, el: unknown) {
    if (el) {
        modelSelectRefs.value[index] = el as SelectComponentExpose
    } else {
        delete modelSelectRefs.value[index]
    }
}

function supportsModel(provider?: string) {
    return !!provider && MODEL_PROVIDERS.has(provider.toLowerCase())
}

function supportsInstructions(provider?: string) {
    return !!provider && instructionProviders.value.has(provider.toLowerCase())
}

function isModelField(key: string, type: string): boolean {
    if (type === PLUGIN_SETTING_TYPE.REMOTE_DROPDOWN) return true
    const normalized = key.toLowerCase()
    return normalized.endsWith('_model') || normalized.includes('_model') || normalized === 'model'
}

const dragFrom = ref<number | null>(null)
const dropIndex = ref<number | null>(null)

function rowClass(entry: ChainEntry, index: number) {
    return {
        'border-accent bg-accent/10': configuringIndex.value === index && dragFrom.value !== index,
        'border-accent/30': configuringIndex.value !== index,
        'ring-accent ring-2': focusedProvider.value.toLowerCase() === entry.provider.toLowerCase(),
        'opacity-40': dragFrom.value === index,
        'border-t-accent border-t-2': dropIndex.value === index && dragFrom.value !== index
    }
}

function movedIndex(current: number, from: number, to: number): number {
    if (current === from) return to
    if (from < current && to >= current) return current - 1
    if (from > current && to <= current) return current + 1
    return current
}

function reindexReactive<T>(source: Record<number, T>, from: number, to: number, length: number) {
    const order = Array.from({ length }, (_, index) => index)
    const [moved] = order.splice(from, 1)
    order.splice(to, 0, moved)
    const snapshot: Record<number, T> = {}
    order.forEach((oldIndex, newIndex) => {
        if (Object.prototype.hasOwnProperty.call(source, oldIndex)) {
            snapshot[newIndex] = source[oldIndex]
        }
    })
    for (const key of Object.keys(source)) {
        delete source[Number(key)]
    }
    Object.assign(source, snapshot)
}

function reorderLocal(from: number, to: number) {
    if (from === to || to < 0 || to >= chain.value.length) return
    const length = chain.value.length
    const next = [...chain.value]
    const [item] = next.splice(from, 1)
    next.splice(to, 0, item)
    chain.value = next
    configuringIndex.value = movedIndex(configuringIndex.value, from, to)
    reindexReactive(modelOptions, from, to, length)
    reindexReactive(modelError, from, to, length)
}

function onDragStart(index: number, event: DragEvent) {
    dragFrom.value = index
    dropIndex.value = index
    event.dataTransfer?.setData('text/plain', chain.value[index]?.id ?? String(index))
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move'
}

function onDragOver(index: number, event: DragEvent) {
    const from = dragFrom.value
    if (from === null) return
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'move'
    const row = event.currentTarget as HTMLElement
    const rect = row.getBoundingClientRect()
    const placeAfter = event.clientY > rect.top + rect.height / 2
    let insertAt = index + (placeAfter ? 1 : 0)
    if (from < insertAt) insertAt -= 1
    dropIndex.value = insertAt
}

function onDrop() {
    void finishDrag()
}

function onDragEnd() {
    void finishDrag()
}

async function finishDrag() {
    const from = dragFrom.value
    const to = dropIndex.value
    dragFrom.value = null
    dropIndex.value = null
    if (from === null || to === null || from === to) return
    reorderLocal(from, to)
    await save(chain.value)
}

function onHandleKeydown(index: number, event: KeyboardEvent) {
    if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return
    event.preventDefault()
    const target = event.key === 'ArrowUp' ? index - 1 : index + 1
    if (target < 0 || target >= chain.value.length) return
    reorderLocal(index, target)
    void save(chain.value)
}

function selectRow(index: number) {
    configuringIndex.value = index
}

const credentialsManifest = computed<IPluginManifest | null>(() => {
    const manifest = configuringManifest.value
    if (!manifest) return null
    const provider = chain.value[configuringIndex.value]?.provider
    if (!supportsModel(provider)) return manifest
    return {
        ...manifest,
        settings: manifest.settings.filter((field) => !isModelField(field.key, field.type))
    }
})

const configuringLabel = computed(() => {
    const value = chain.value[configuringIndex.value]?.provider
    return providerOptions.value.find((option) => option.value === value)?.label ?? value
})

const systemProfiles = computed(() =>
    promptProfiles.value.filter(
        (profile) => profile.type === 'system' && profile.currentPublishedVersionId
    )
)
const contextProfiles = computed(() =>
    promptProfiles.value.filter(
        (profile) => profile.type === 'context' && profile.currentPublishedVersionId
    )
)
const activeSystemProfileName = computed(
    () =>
        systemProfiles.value.find((profile) => profile.id === activeSystemProfileId.value)?.name ??
        'None'
)
const activeContextProfileName = computed(
    () =>
        contextProfiles.value.find((profile) => profile.id === activeContextProfileId.value)?.name ??
        'None'
)

function parseChain(raw: unknown): ChainEntry[] {
    try {
        const text = (raw as string) ?? '[]'
        if (!text.trim().startsWith('[')) {
            return [
                {
                    id: newRowId(),
                    provider: text.trim() || SERVICE_TYPE.LIBRETRANSLATE
                }
            ]
        }
        const parsed = JSON.parse(text) as unknown[]
        if (!Array.isArray(parsed) || parsed.length === 0) {
            return [{ id: newRowId(), provider: SERVICE_TYPE.LIBRETRANSLATE }]
        }
        return parsed.map((item) => {
            if (typeof item === 'string') return { id: newRowId(), provider: item }
            const obj = item as {
                id?: string
                provider?: string
                service?: string
                model?: string
                systemPromptProfileId?: number
                contextPromptProfileId?: number
            }
            return {
                id: obj.id || newRowId(),
                provider: obj.provider || obj.service || SERVICE_TYPE.LIBRETRANSLATE,
                model: obj.model || null,
                systemPromptProfileId: obj.systemPromptProfileId ?? null,
                contextPromptProfileId: obj.contextPromptProfileId ?? null
            }
        })
    } catch {
        return [{ id: newRowId(), provider: SERVICE_TYPE.LIBRETRANSLATE }]
    }
}

async function save(next: ChainEntry[]) {
    chain.value = next
    const payload = next.map((entry) => ({
        id: entry.id,
        provider: entry.provider,
        ...(entry.model ? { model: entry.model } : {}),
        ...(entry.systemPromptProfileId
            ? { systemPromptProfileId: entry.systemPromptProfileId }
            : {}),
        ...(entry.contextPromptProfileId
            ? { contextPromptProfileId: entry.contextPromptProfileId }
            : {})
    }))
    await settingsStore.updateSetting(SETTINGS.SERVICE_TYPE, JSON.stringify(payload), true)
    for (const entry of next) {
        if (!entry.model || !supportsModel(entry.provider)) continue
        const key = modelSettingKey(entry.provider)
        if (key) {
            await settingsStore.updateSetting(key as any, entry.model, true)
        }
    }
    saveNotification.value?.show()
}

function modelSettingKey(provider: string): string | null {
    const map: Record<string, string> = {
        openai: SETTINGS.OPENAI_MODEL,
        anthropic: SETTINGS.ANTHROPIC_MODEL,
        gemini: SETTINGS.GEMINI_MODEL,
        deepseek: SETTINGS.DEEPSEEK_MODEL,
        localai: SETTINGS.LOCAL_AI_MODEL,
        openrouter: (SETTINGS as any).OPENROUTER_MODEL,
        zai: (SETTINGS as any).ZAI_MODEL,
        'opencode-go': (SETTINGS as any).OPENCODE_GO_MODEL,
        qwen: (SETTINGS as any).QWEN_MODEL,
        'qwen-mt': (SETTINGS as any).QWEN_MT_MODEL,
        xai: (SETTINGS as any).XAI_MODEL,
        'xai-oauth': (SETTINGS as any).XAI_OAUTH_MODEL,
        mistral: (SETTINGS as any).MISTRAL_MODEL
    }
    return map[provider.toLowerCase()] ?? null
}

function setProvider(index: number, value: string) {
    const next = chain.value.map((e, i) =>
        i === index
            ? {
                  ...e,
                  provider: value,
                  model: supportsModel(value) ? e.model : null,
                  systemPromptProfileId: supportsInstructions(value)
                      ? e.systemPromptProfileId
                      : null,
                  contextPromptProfileId: supportsInstructions(value)
                      ? e.contextPromptProfileId
                      : null
              }
            : e
    )
    configuringIndex.value = index
    save(next)
    loadModels(index, false)
}

function setPromptProfile(index: number, type: 'system' | 'context', value: string) {
    const profileId = value ? Number(value) : null
    const next = chain.value.map((entry, rowIndex) => {
        if (rowIndex !== index) return entry
        return type === 'system'
            ? { ...entry, systemPromptProfileId: profileId }
            : { ...entry, contextPromptProfileId: profileId }
    })
    save(next)
}

function setModel(index: number, value: string) {
    const next = chain.value.map((e, i) => (i === index ? { ...e, model: value } : e))
    save(next)
}

function addRow() {
    const preferred =
        providerOptions.value.find((o) => o.value === 'microsoft')?.value ||
        providerOptions.value[0]?.value ||
        SERVICE_TYPE.LIBRETRANSLATE
    const next = [...chain.value, { id: newRowId(), provider: preferred }]
    configuringIndex.value = next.length - 1
    save(next)
}

function removeRow(index: number) {
    if (index === 0 || chain.value.length <= 1) return
    const next = chain.value.filter((_, i) => i !== index)
    configuringIndex.value = Math.min(configuringIndex.value, next.length - 1)
    save(next)
}

async function loadManifest(provider: string) {
    const request = ++manifestRequest
    const selected = () => chain.value[configuringIndex.value]?.provider?.toLowerCase()
    if (selected() !== provider.toLowerCase()) return
    configuringManifest.value = null
    manifestError.value = null
    try {
        const manifest = await servicesApi.plugin.getManifest(provider)
        if (request !== manifestRequest || selected() !== provider.toLowerCase()) return
        await settingsStore.setPluginSettings(manifest.settings)
        if (request !== manifestRequest || selected() !== provider.toLowerCase()) return
        configuringManifest.value = manifest
        manifestError.value = null
    } catch (error) {
        if (request !== manifestRequest) return
        console.error('Failed to load manifest', error)
        configuringManifest.value = null
        manifestError.value = `No manifest available for ${provider}.`
    }
}

async function loadModels(index: number, refresh: boolean) {
    const provider = chain.value[index]?.provider
    if (!provider || !supportsModel(provider)) return
    modelSelectRefs.value[index]?.setLoadingState(true)
    try {
        modelError[index] = null
        const endpoint = `/api/plugin/${provider}/models${refresh ? '?refresh=true' : ''}`
        const response = await servicesApi.plugin.getOptions(endpoint)
        modelOptions[index] = (response.options || []).map((o: any) => ({
            value: o.value,
            label: o.label
        }))
        if (!modelOptions[index].length) {
            modelError[index] = response.message || 'No models returned'
        }
    } catch (e) {
        console.error(e)
        modelError[index] = 'Error loading models'
    } finally {
        modelSelectRefs.value[index]?.setLoadingState(false)
    }
}

watch(
    () => settingsStore.getSetting(SETTINGS.SERVICE_TYPE),
    (raw) => {
        chain.value = parseChain(raw)
    }
)

const focusProviderFromHash = async () => {
    const hash = route.hash
    if (!hash.startsWith('#service-')) return
    const provider = decodeURIComponent(hash.slice('#service-'.length))
    focusedProvider.value = provider
    const index = chain.value.findIndex(
        (entry) => entry.provider.toLowerCase() === provider.toLowerCase()
    )
    if (index >= 0) configuringIndex.value = index
    await nextTick()
    const row = document.getElementById(`service-${provider}`)
    const target = row ?? document.getElementById('translation-services')
    target?.scrollIntoView({ behavior: 'smooth', block: 'center' })
}

watch(() => route.hash, focusProviderFromHash)

onMounted(async () => {
    chain.value = parseChain(settingsStore.getSetting(SETTINGS.SERVICE_TYPE))
    await focusProviderFromHash()
    try {
        const summaries: IPluginSummary[] = await servicesApi.plugin.list()
        instructionProviders.value = new Set(
            summaries
                .filter((summary) => summary.supportsInstructionProfiles)
                .map((summary) => summary.provider.toLowerCase())
        )
        providerOptions.value = summaries
            .filter((summary) => summary.isBuiltIn || summary.capabilities?.includes('translation'))
            .map((s) => ({ value: s.provider, label: s.displayName }))
            .sort((a, b) => a.label.localeCompare(b.label))
    } catch (error) {
        console.error('Failed to load translation provider list', error)
    }
    try {
        const [profiles, activeSystem, activeContext] = await Promise.all([
            servicesApi.promptProfile.list(),
            servicesApi.setting.getSetting<string>(SETTINGS.ACTIVE_SYSTEM_PROMPT_PROFILE_ID),
            servicesApi.setting.getSetting<string>(SETTINGS.ACTIVE_CONTEXT_PROMPT_PROFILE_ID)
        ])
        promptProfiles.value = profiles
        activeSystemProfileId.value = Number(activeSystem) || 0
        activeContextProfileId.value = Number(activeContext) || 0
    } catch (error) {
        console.error('Failed to load prompt profiles', error)
    }
    chain.value.forEach((e, i) => {
        if (supportsModel(e.provider)) loadModels(i, false)
    })
})

watch(
    () => chain.value[configuringIndex.value]?.provider,
    (provider) => {
        if (provider) loadManifest(provider)
    },
    { immediate: true }
)
</script>
