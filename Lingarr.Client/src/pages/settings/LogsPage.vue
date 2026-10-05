<template>
    <div class="w-full">
        <SettingsSectionTabs section="system" />
        <div class="bg-secondary p-4">
            <div class="border-secondary bg-primary mb-4 border-b-2">
                <div
                    class="flex flex-col gap-3 px-4 py-3 lg:flex-row lg:items-center lg:justify-between">
                    <h1 class="text-xl font-bold">System Logs</h1>
                    <div class="flex flex-wrap items-center justify-end gap-2">
                        <select
                            v-model="minLevel"
                            class="border-secondary bg-secondary text-accent-content rounded border px-2 py-1 text-sm"
                            aria-label="Log level"
                            @change="loadLatest">
                            <option value="">All levels</option>
                            <option value="Warning">Warnings and errors</option>
                            <option value="Error">Errors only</option>
                        </select>
                        <button
                            type="button"
                            class="cursor-pointer rounded px-3 py-1 text-sm font-medium transition"
                            :class="
                                follow
                                    ? 'bg-accent text-white'
                                    : 'bg-secondary text-accent-content'
                            "
                            :aria-pressed="follow"
                            @click="toggleFollow">
                            {{ follow ? 'Following' : 'Follow' }}
                        </button>
                        <button
                            type="button"
                            class="bg-secondary text-accent-content cursor-pointer rounded px-3 py-1 text-sm font-medium transition"
                            @click="jumpToLatest">
                            Latest
                        </button>
                        <button
                            type="button"
                            class="hover:bg-accent/80 bg-accent cursor-pointer rounded px-3 py-1 text-sm font-medium text-white transition"
                            @click="exportLogs">
                            Export
                        </button>
                        <button
                            type="button"
                            class="bg-error hover:bg-error/80 cursor-pointer rounded px-3 py-1 text-sm font-medium text-white transition"
                            @click="clearLogs">
                            Clear
                        </button>
                    </div>
                </div>
            </div>

            <div
                ref="logContainer"
                class="bg-primary text-accent-content h-[70vh] overflow-y-auto font-mono text-sm"
                @scroll="onScroll">
                <div
                    v-if="loading && logs.length === 0"
                    class="flex h-full items-center justify-center text-gray-400">
                    Loading logs.
                </div>
                <div
                    v-else-if="loadError && logs.length === 0"
                    class="flex h-full items-center justify-center text-red-400">
                    {{ loadError }}
                </div>
                <div
                    v-else-if="logs.length === 0"
                    class="flex h-full items-center justify-center text-gray-400">
                    No lines for this view.
                </div>
                <template v-else>
                    <div
                        v-if="hasOlder"
                        class="text-secondary-content px-4 py-2 text-center text-xs">
                        {{ loadingOlder ? 'Loading older lines.' : 'Scroll up for older lines.' }}
                    </div>
                    <article
                        v-for="log in logs"
                        :key="log.id"
                        class="border-secondary/30 hover:bg-secondary/20 border-b border-l-2 py-2 pr-4 pl-3"
                        :class="rowClass(log.logLevel)">
                        <div class="flex gap-3">
                            <time class="w-20 shrink-0 whitespace-nowrap text-gray-400">{{ lineTime(log) }}</time>
                            <span
                                class="w-10 shrink-0 text-xs font-semibold"
                                :class="levelClass(log.logLevel)">
                                {{ levelLabel(log.logLevel) }}
                            </span>
                            <div class="min-w-0 flex-1">
                                <div class="truncate text-xs text-blue-300/80">
                                    {{ log.formattedSource }}
                                </div>
                                <p class="break-words whitespace-pre-wrap">
                                    <span
                                        v-for="(part, index) in messageParts(log.message)"
                                        :key="index"
                                        :class="partClass(part.tone)">
                                        {{ part.text }}
                                    </span>
                                </p>
                                <p v-if="log.hint" class="mt-1 text-sm text-amber-200">
                                    Next: {{ log.hint }}
                                </p>
                                <button
                                    v-if="log.exception"
                                    type="button"
                                    class="mt-1 cursor-pointer text-xs text-gray-400 underline"
                                    @click="toggleDetails(log.id)">
                                    {{ openDetails[log.id] ? 'Hide details' : 'Show details' }}
                                </button>
                                <pre
                                    v-if="log.exception && openDetails[log.id]"
                                    class="mt-1 whitespace-pre-wrap text-xs text-gray-300">{{ log.exception }}</pre>
                            </div>
                        </div>
                    </article>
                </template>
            </div>

            <div
                class="border-secondary bg-primary text-secondary-content mt-4 flex flex-wrap justify-between gap-2 border-t-2 px-4 py-2 text-sm">
                <div>
                    {{ logs.length }} lines loaded
                    <span v-if="hasOlder">· scroll up for older lines</span>
                    <span v-else>· start of the log</span>
                </div>
                <div>
                    <span v-if="loadError && logs.length > 0">{{ loadError }}</span>
                    <span v-else-if="reconnecting">Reconnecting.</span>
                    <span v-else>{{ follow ? 'Following new lines.' : 'Follow is off.' }}</span>
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, nextTick } from 'vue'
import { ILogEntry, ILogPage } from '@/ts'
import services from '@/services'
import SettingsSectionTabs from '@/components/features/settings/SettingsSectionTabs.vue'

const pageSize = 80
const logs = ref<ILogEntry[]>([])
const hasOlder = ref(false)
const loading = ref(true)
const loadingOlder = ref(false)
const loadError = ref('')
const follow = ref(true)
const reconnecting = ref(false)
const minLevel = ref('')
const logContainer = ref<HTMLElement | null>(null)
const openDetails = ref<Record<number, boolean>>({})

let pageRequest = 0
let streamGeneration = 0
let eventSource: EventSource | null = null
let pinning = false

interface MessagePart {
    text: string
    tone: 'plain' | 'good' | 'bad' | 'warn'
}

const messageParts = (message: string): MessagePart[] => {
    const source = message ?? ''
    const pattern = /\|(Green|Red|Orange)\|([\s\S]*?)\|\/\1\|/g
    const parts: MessagePart[] = []
    let cursor = 0
    for (const match of source.matchAll(pattern)) {
        const start = match.index ?? 0
        if (start > cursor) {
            parts.push({ text: source.slice(cursor, start), tone: 'plain' })
        }
        const tone = match[1] === 'Green' ? 'good' : match[1] === 'Red' ? 'bad' : 'warn'
        parts.push({ text: match[2], tone })
        cursor = start + match[0].length
    }
    if (cursor < source.length || parts.length === 0) {
        parts.push({ text: source.slice(cursor), tone: 'plain' })
    }
    return parts
}

const partClass = (tone: MessagePart['tone']): string => {
    if (tone === 'good') return 'text-green-500'
    if (tone === 'bad') return 'text-red-400'
    if (tone === 'warn') return 'text-orange-400'
    return ''
}

const levelLabel = (level: string): string => {
    const value = level.toLowerCase()
    if (value === 'warning') return 'WRN'
    if (value === 'error') return 'ERR'
    if (value === 'critical') return 'CRT'
    if (value === 'information') return 'INF'
    return level.slice(0, 3).toUpperCase()
}

const levelClass = (level: string): string => {
    const value = level.toLowerCase()
    if (value === 'error' || value === 'critical') return 'text-red-400'
    if (value === 'warning') return 'text-orange-400'
    return 'text-green-500'
}

const rowClass = (level: string): string => {
    const value = level.toLowerCase()
    if (value === 'error' || value === 'critical') return 'border-red-500 bg-red-500/5'
    if (value === 'warning') return 'border-amber-400'
    return 'border-transparent'
}

const lineTime = (log: ILogEntry): string => {
    if (!log.timestamp) {
        return log.formattedTime
    }
    const date = new Date(log.timestamp)
    if (Number.isNaN(date.getTime())) {
        return log.formattedTime
    }
    return date.toLocaleTimeString([], {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hourCycle: 'h23'
    })
}

const pinToBottom = async () => {
    pinning = true
    await nextTick()
    if (logContainer.value) {
        logContainer.value.scrollTop = logContainer.value.scrollHeight
    }
    pinning = false
}

const closeStream = () => {
    streamGeneration += 1
    eventSource?.close()
    eventSource = null
}

const openStream = (after: number) => {
    streamGeneration += 1
    eventSource?.close()
    const generation = streamGeneration
    const source = services.logs.getStream(after, minLevel.value || undefined)
    eventSource = source
    source.addEventListener('log', (event) => {
        if (generation !== streamGeneration) {
            return
        }
        try {
            const entry = JSON.parse((event as MessageEvent).data) as ILogEntry
            const last = logs.value[logs.value.length - 1]
            if (last && entry.id <= last.id) {
                return
            }
            logs.value.push(entry)
            reconnecting.value = false
            if (follow.value) {
                void pinToBottom()
            }
        } catch (error) {
            console.error('Error processing log entry:', error)
        }
    })
    source.onerror = () => {
        if (generation !== streamGeneration) {
            return
        }
        reconnecting.value = true
        streamGeneration += 1
        source.close()
        const scheduled = streamGeneration
        window.setTimeout(() => {
            if (scheduled !== streamGeneration) {
                return
            }
            const newest = logs.value.length ? logs.value[logs.value.length - 1].id : after
            openStream(newest)
        }, 5000)
    }
}

const loadLatest = async () => {
    const request = ++pageRequest
    loading.value = true
    loadError.value = ''
    try {
        const page = await services.logs.getPage<ILogPage>({
            limit: pageSize,
            minLevel: minLevel.value || undefined
        })
        if (request !== pageRequest) {
            return
        }
        logs.value = page.items
        hasOlder.value = page.hasOlder
        openStream(page.newestId ?? page.latestId)
        await pinToBottom()
    } catch {
        if (request === pageRequest) {
            loadError.value = 'Logs could not be loaded.'
        }
    } finally {
        if (request === pageRequest) {
            loading.value = false
        }
    }
}

const loadOlder = async () => {
    if (!hasOlder.value || loadingOlder.value || logs.value.length === 0) {
        return
    }
    const request = pageRequest
    const oldestId = logs.value[0].id
    loadingOlder.value = true
    const pane = logContainer.value
    const previousHeight = pane?.scrollHeight ?? 0
    const previousTop = pane?.scrollTop ?? 0
    try {
        const page = await services.logs.getPage<ILogPage>({
            limit: pageSize,
            before: oldestId,
            minLevel: minLevel.value || undefined
        })
        if (request !== pageRequest) {
            return
        }
        const seen = new Set(logs.value.map((line) => line.id))
        const older = page.items.filter((line) => !seen.has(line.id))
        logs.value = [...older, ...logs.value]
        hasOlder.value = page.hasOlder
        await nextTick()
        if (pane) {
            pane.scrollTop = pane.scrollHeight - previousHeight + previousTop
        }
    } catch {
        // The next scroll up tries again.
    } finally {
        if (request === pageRequest) {
            loadingOlder.value = false
        }
    }
}

const onScroll = () => {
    const pane = logContainer.value
    if (!pane || pinning) {
        return
    }
    const distance = pane.scrollHeight - pane.scrollTop - pane.clientHeight
    follow.value = distance < 64
    if (pane.scrollTop < 48) {
        void loadOlder()
    }
}

const toggleFollow = () => {
    follow.value = !follow.value
    if (follow.value) {
        void pinToBottom()
    }
}

const jumpToLatest = () => {
    follow.value = true
    void pinToBottom()
}

const toggleDetails = (id: number) => {
    openDetails.value = { ...openDetails.value, [id]: !openDetails.value[id] }
}

const clearLogs = async () => {
    try {
        await services.logs.clear()
        logs.value = []
        hasOlder.value = false
        openDetails.value = {}
    } catch {
        loadError.value = 'Logs could not be cleared.'
    }
}

const exportLogs = () => {
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-')
    let content = `System Logs Export\nGenerated: ${new Date().toLocaleString()}\nLines: ${logs.value.length}\n${'='.repeat(80)}\n\n`
    logs.value.forEach((log) => {
        content += `[${lineTime(log)}] [${log.logLevel}] [${log.category}] ${log.message}\n`
        if (log.hint) {
            content += `Next: ${log.hint}\n`
        }
        if (log.exception) {
            content += `${log.exception}\n`
        }
        content += '\n'
    })
    const blob = new Blob([content], { type: 'text/plain' })
    const url = window.URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `system-logs-${timestamp}.txt`
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    window.URL.revokeObjectURL(url)
}

onMounted(() => {
    void loadLatest()
})

onUnmounted(() => {
    pageRequest += 1
    closeStream()
})
</script>
