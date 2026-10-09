<template>
    <section
        class="border-accent/25 bg-primary/35 space-y-3 rounded-md border p-3"
        aria-labelledby="xai-oauth-heading">
        <div class="flex flex-wrap items-center justify-between gap-2">
            <div>
                <h4 id="xai-oauth-heading" class="text-sm font-semibold">
                    xAI account connection
                </h4>
                <p class="text-primary-content/60 text-xs">
                    Experimental. Requires an eligible SuperGrok or X Premium+ subscription.
                </p>
            </div>
            <span
                class="rounded-md border px-2 py-1 text-xs font-semibold"
                :class="
                    connected
                        ? 'border-success/40 bg-success/10 text-success'
                        : 'border-accent/30 text-primary-content/65'
                "
                role="status">
                {{ statusLabel }}
            </span>
        </div>

        <div v-if="device" class="space-y-2">
            <p class="text-primary-content/70 text-xs">
                Enter this code on the xAI approval page:
            </p>
            <div
                class="border-accent/40 bg-secondary rounded-md border px-3 py-3 text-center font-mono text-xl font-semibold tracking-[0.15em]"
                aria-label="xAI device code">
                {{ device.userCode }}
            </div>
            <a
                :href="approvalUrl"
                target="_blank"
                rel="noopener noreferrer"
                class="text-accent block break-all text-xs underline">
                {{ approvalUrl }}
            </a>
        </div>

        <p v-if="error" class="text-error text-xs" role="alert">{{ error }}</p>

        <div class="flex flex-wrap gap-2">
            <ButtonComponent
                v-if="!connected"
                size="xs"
                :disabled="busy"
                @click="startConnection">
                {{ device ? 'Restart login' : 'Connect with xAI' }}
            </ButtonComponent>
            <ButtonComponent
                v-if="device && !connected"
                variant="ghost"
                size="xs"
                @click="openApproval">
                Open approval page
            </ButtonComponent>
            <ButtonComponent
                v-if="connected"
                variant="ghost"
                size="xs"
                :disabled="busy"
                @click="disconnect">
                Disconnect
            </ButtonComponent>
        </div>
    </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { IXaiOAuthDevice } from '@/ts'
import services from '@/services'
import ButtonComponent from '@/components/common/ButtonComponent.vue'

const emit = defineEmits(['save'])
const connected = ref(false)
const busy = ref(false)
const waiting = ref(false)
const error = ref<string | null>(null)
const device = ref<IXaiOAuthDevice | null>(null)
let pollTimer: ReturnType<typeof setTimeout> | null = null

const approvalUrl = computed(
    () => device.value?.verificationUriComplete || device.value?.verificationUri || ''
)
const statusLabel = computed(() => {
    if (connected.value) return 'Connected'
    if (busy.value && !waiting.value) return 'Starting…'
    if (waiting.value) return 'Waiting for approval…'
    return 'Not connected'
})

function clearPoll() {
    if (pollTimer) {
        clearTimeout(pollTimer)
        pollTimer = null
    }
}

function openApproval() {
    if (approvalUrl.value) {
        window.open(approvalUrl.value, '_blank', 'noopener,noreferrer')
    }
}

function schedulePoll(intervalSeconds: number) {
    clearPoll()
    pollTimer = setTimeout(async () => {
        if (!device.value) return
        try {
            const result = await services.xaiOAuth.poll(device.value.flowId)
            if (result.status === 'connected') {
                connected.value = true
                waiting.value = false
                busy.value = false
                device.value = null
                emit('save')
                return
            }
            if (result.status === 'pending') {
                schedulePoll(Math.max(3, result.intervalSeconds || intervalSeconds))
                return
            }
            waiting.value = false
            busy.value = false
            error.value = result.message || 'xAI login did not complete.'
        } catch {
            waiting.value = false
            busy.value = false
            error.value = 'Lingarr Next could not check the xAI login status.'
        }
    }, Math.max(3, intervalSeconds) * 1000)
}

async function startConnection() {
    clearPoll()
    busy.value = true
    waiting.value = false
    error.value = null
    try {
        device.value = await services.xaiOAuth.start()
        waiting.value = true
        openApproval()
        schedulePoll(device.value.intervalSeconds)
    } catch (response: any) {
        busy.value = false
        error.value =
            response?.data?.detail ||
            response?.data?.title ||
            'Lingarr Next could not start xAI device login.'
    }
}

async function disconnect() {
    clearPoll()
    busy.value = true
    error.value = null
    try {
        await services.xaiOAuth.disconnect()
        connected.value = false
        waiting.value = false
        device.value = null
        emit('save')
    } catch {
        error.value = 'Lingarr Next could not disconnect the xAI account.'
    } finally {
        busy.value = false
    }
}

onMounted(async () => {
    try {
        const status = await services.xaiOAuth.status()
        connected.value = status.connected
    } catch {
        error.value = 'Lingarr Next could not read the xAI connection status.'
    }
})

onBeforeUnmount(clearPoll)
</script>
