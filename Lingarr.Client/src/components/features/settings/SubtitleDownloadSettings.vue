<template>
    <CardComponent title="Providers" :muted="enabled !== 'true'">
        <template #description>
            Download existing subtitles before translation. A match in the target language is used as-is.
        </template>
        <template #actions>
            <ToggleButton
                v-model="enabled"
                aria-label="Enabled">
                <span class="text-primary-content text-sm font-medium">
                    {{ enabled === 'true' ? 'On' : 'Off' }}
                </span>
            </ToggleButton>
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />

            <div class="space-y-2">
                <ol id="subtitle-download-providers" class="space-y-3">
                    <li
                        v-for="(entry, index) in chain"
                        :key="entry.id"
                        :id="`subtitle-provider-${entry.provider}`"
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
                            <SelectComponent
                                :selected="entry.provider"
                                :options="providerOptions"
                                placeholder="Select provider..."
                                @update:selected="(value: string) => setProvider(index, value)" />
                        </div>

                        <div class="mt-2 flex h-6 w-6 shrink-0 items-center justify-center">
                            <button
                                type="button"
                                class="text-primary-content hover:text-primary-content/50 focus-visible:ring-accent cursor-pointer rounded p-1 transition-colors focus-visible:ring-2 focus-visible:outline-none"
                                title="Remove provider"
                                aria-label="Remove provider"
                                @click.stop="removeRow(index)">
                                <TrashIcon class="h-4 w-4" />
                            </button>
                        </div>
                    </li>
                    <li>
                        <ButtonComponent variant="ghost" size="xs" @click="addRow">
                            <PlusIcon class="mr-1 h-3 w-3" />
                            Add subtitle provider
                        </ButtonComponent>
                    </li>
                </ol>
            </div>

            <div v-if="chain.length" class="border-accent/30 mt-4 space-y-3 border-t pt-4">
                <div class="text-sm">
                    <span class="text-secondary-content/60">Selected</span>
                    <span class="ml-1 font-semibold">{{ configuringLabel }}</span>
                    <span class="text-secondary-content/60 ml-1">
                        (row {{ configuringIndex + 1 }})
                    </span>
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
            </div>

            <div class="border-accent/30 mt-4 flex flex-col gap-2 border-t pt-4">
                <button
                    class="w-fit cursor-pointer rounded border border-accent px-3 py-2 text-sm transition-colors hover:bg-accent hover:text-white disabled:cursor-not-allowed disabled:opacity-50"
                    type="button"
                    :disabled="enabled !== 'true' || chain.length === 0 || sweepStatus == 'running'"
                    @click="startSweep">
                    {{ sweepStatus == 'running' ? 'Downloading...' : 'Download missing English' }}
                </button>
                <p class="text-sm text-secondary-content">
                    Lingarr asks configured providers for an English subtitle on every title that
                    does not have one. It does not translate. Results appear in System logs.
                </p>
                <p v-if="sweepNotice" class="text-sm text-secondary-content">{{ sweepNotice }}</p>
                <p v-if="sweepSummary" class="text-sm text-secondary-content">{{ sweepSummary }}</p>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import axios from 'axios'
import { useSettingStore } from '@/store/setting'
import { IPluginManifest, SETTINGS } from '@/ts'
import CardComponent from '@/components/common/CardComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import DynamicPluginForm from '@/components/features/settings/DynamicPluginForm.vue'
import TrashIcon from '@/components/icons/TrashIcon.vue'
import PlusIcon from '@/components/icons/PlusIcon.vue'

type ChainEntry = {
    id: string
    provider: string
}

function newRowId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return crypto.randomUUID()
    }
    return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const providerOptions = ref<{ value: string; label: string }[]>([])
const chain = ref<ChainEntry[]>([])
const configuringIndex = ref(0)
const configuringManifest = ref<IPluginManifest | null>(null)
const manifestError = ref<string | null>(null)
let manifestRequest = 0

const dragFrom = ref<number | null>(null)
const dropIndex = ref<number | null>(null)
const sweepStatus = ref('idle')
const sweepSummary = ref('')
const sweepNotice = ref('')
let sweepTimer = 0

const enabled = computed({
    get: () => (settingsStore.getSetting(SETTINGS.SUBTITLE_DOWNLOADERS_ENABLED) as string) || 'true',
    set: (value: string) => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_DOWNLOADERS_ENABLED, value, true)
    }
})

function rowClass(_entry: ChainEntry, index: number) {
    return {
        'border-accent bg-accent/10': configuringIndex.value === index && dragFrom.value !== index,
        'border-accent/30': configuringIndex.value !== index,
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

function reorderLocal(from: number, to: number) {
    if (from === to || to < 0 || to >= chain.value.length) return
    const next = [...chain.value]
    const [item] = next.splice(from, 1)
    next.splice(to, 0, item)
    chain.value = next
    configuringIndex.value = movedIndex(configuringIndex.value, from, to)
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

const credentialsManifest = computed<IPluginManifest | null>(() => configuringManifest.value)

const configuringLabel = computed(() => {
    const value = chain.value[configuringIndex.value]?.provider
    return providerOptions.value.find((option) => option.value === value)?.label ?? value
})

function parseChain(raw: unknown): ChainEntry[] {
    try {
        const text = typeof raw === 'string' ? raw : ''
        if (!text.trim().startsWith('[')) {
            return text.trim() ? [{ id: newRowId(), provider: text.trim() }] : []
        }
        const parsed = JSON.parse(text) as unknown[]
        if (!Array.isArray(parsed)) return []
        return parsed
            .map((item) => {
                if (typeof item === 'string') return { id: newRowId(), provider: item }
                const obj = item as { id?: string; provider?: string }
                return {
                    id: obj.id || newRowId(),
                    provider: obj.provider || ''
                }
            })
            .filter((entry) => entry.provider)
    } catch {
        return []
    }
}

async function save(next: ChainEntry[]) {
    chain.value = next
    const payload = next.map((entry) => ({
        id: entry.id,
        provider: entry.provider
    }))
    await settingsStore.updateSetting(
        SETTINGS.SUBTITLE_DOWNLOAD_PROVIDERS,
        JSON.stringify(payload),
        true
    )
    saveNotification.value?.show()
}

function setProvider(index: number, value: string) {
    const next = chain.value.map((entry, row) =>
        row === index ? { ...entry, provider: value } : entry
    )
    configuringIndex.value = index
    void save(next)
}

function addRow() {
    const preferred = providerOptions.value[0]?.value || 'opensubtitlescom'
    const next = [...chain.value, { id: newRowId(), provider: preferred }]
    configuringIndex.value = next.length - 1
    void save(next)
}

function removeRow(index: number) {
    const next = chain.value.filter((_, row) => row !== index)
    configuringIndex.value = Math.min(configuringIndex.value, Math.max(0, next.length - 1))
    void save(next)
}

async function loadManifest(provider: string) {
    const request = ++manifestRequest
    const selected = () => chain.value[configuringIndex.value]?.provider?.toLowerCase()
    if (!provider || selected() !== provider.toLowerCase()) return
    configuringManifest.value = null
    manifestError.value = null
    try {
        const { data } = await axios.get<IPluginManifest>(`/api/subtitle-download/${provider}`)
        if (request !== manifestRequest || selected() !== provider.toLowerCase()) return
        await settingsStore.setPluginSettings(data.settings ?? [])
        if (request !== manifestRequest || selected() !== provider.toLowerCase()) return
        configuringManifest.value = data
        manifestError.value = null
    } catch (error) {
        if (request !== manifestRequest) return
        console.error('Failed to load subtitle provider', error)
        configuringManifest.value = null
        manifestError.value = `No manifest available for ${provider}.`
    }
}

watch(
    () => settingsStore.getSetting(SETTINGS.SUBTITLE_DOWNLOAD_PROVIDERS),
    (raw) => {
        chain.value = parseChain(raw)
    }
)

async function refreshSweep() {
    const { data } = await axios.get<{ status: string; summary: string }>(
        '/api/subtitle-download/missing-english'
    )
    sweepStatus.value = data.status || 'idle'
    sweepSummary.value = data.summary || ''
    if (sweepStatus.value == 'running' && sweepTimer == 0) {
        sweepTimer = window.setInterval(() => {
            refreshSweep().catch(() => undefined)
        }, 5000)
    }
    if (sweepStatus.value != 'running' && sweepTimer != 0) {
        window.clearInterval(sweepTimer)
        sweepTimer = 0
    }
}

async function startSweep() {
    sweepNotice.value = ''
    try {
        const { data } = await axios.post<{ started: boolean; message: string }>(
            '/api/subtitle-download/missing-english'
        )
        sweepNotice.value = data.message
        await refreshSweep()
    } catch {
        sweepNotice.value = 'Lingarr could not start the English subtitle download.'
    }
}

onMounted(async () => {
    chain.value = parseChain(settingsStore.getSetting(SETTINGS.SUBTITLE_DOWNLOAD_PROVIDERS))
    try {
        const { data } = await axios.get<{ provider: string; displayName: string }[]>(
            '/api/subtitle-download'
        )
        providerOptions.value = data
            .map((item) => ({ value: item.provider, label: item.displayName }))
            .sort((a, b) => a.label.localeCompare(b.label))
    } catch (error) {
        console.error('Failed to load subtitle providers', error)
    }
    await refreshSweep().catch(() => undefined)
})

onUnmounted(() => {
    if (sweepTimer != 0) {
        window.clearInterval(sweepTimer)
        sweepTimer = 0
    }
})

watch(
    () => chain.value[configuringIndex.value]?.provider,
    (provider) => {
        if (provider) void loadManifest(provider)
        else configuringManifest.value = null
    },
    { immediate: true }
)
</script>
