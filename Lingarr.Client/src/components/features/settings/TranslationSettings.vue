<template>
    <CardComponent title="Translation Request">
        <template #description>
            Modify translation request settings by changing retry options or batch size if available in the service.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />

            <template v-if="supportsBatch">
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Use batch translation</span>
                    Process multiple subtitle lines together in batches to improve translation
                    efficiency and context awareness. Note that single-line translations with context
                    are still more reliable and of higher quality.
                </div>
                <ToggleButton v-model="useBatchTranslation">
                    <span class="text-sm font-medium text-primary-content">
                        {{ useBatchTranslation == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>
            </template>

            <template v-if="useBatchTranslation == 'true'">
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Batch size:</span>
                    Amount of subtitle lines in a single batch.
                </div>
                <InputComponent
                    v-model="maxBatchSize"
                    :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                    @update:validation="(val) => (isValid.maxBatchSize = val)" />
            </template>

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Cache quality progress on cancelled tasks</span>
                Keep the lines a cancelled translation already finished. The next
                run for that movie or episode continues from those lines when their
                quality reaches the threshold. Below that, it starts over.
            </div>
            <ToggleButton v-model="cacheCancelledProgress">
                <span class="text-sm font-medium text-primary-content">
                    {{ cacheCancelledProgress == 'true' ? 'Enabled' : 'Disabled' }}
                </span>
            </ToggleButton>

            <template v-if="cacheCancelledProgress == 'true'">
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Quality threshold:</span>
                    Continue when the finished lines score at least this. 90 is
                    comfortably Good. 85 is only the border of Good, so a partial
                    at 85 starts over.
                </div>
                <InputComponent
                    v-model="cacheCancelledQualityThreshold"
                    :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                    @update:validation="(val) => (isValid.cacheCancelledQualityThreshold = val)" />
            </template>

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">{{ requestTimeoutLabel }}:</span>
                Maximum time in minutes to wait for a translation response before the request is
                cancelled. Each provider keeps its own value; Microsoft defaults to 15 minutes
                while other providers default to 5.
            </div>
            <InputComponent
                v-model="requestTimeout"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.requestTimeout = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Max translation retries:</span>
                Maximum number of retries per line or batch.
            </div>
            <InputComponent
                v-model="maxRetries"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.maxRetries = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Delay between retries:</span>
                Initial delay before retrying, in seconds.
            </div>
            <InputComponent
                v-model="retryDelay"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.retryDelay = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Retry delay multiplier:</span>
                Factor by which the delay increases after each retry.
            </div>
            <InputComponent
                v-model="retryDelayMultiplier"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.retryDelayMultiplier = val)" />
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, ref, reactive } from 'vue'
import { useSettingStore } from '@/store/setting'
import { INPUT_VALIDATION_TYPE, ISettings, SERVICE_TYPE, SETTINGS } from '@/ts'
import CardComponent from '@/components/common/CardComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const isValid = reactive({
    maxBatchSize: true,
    requestTimeout: true,
    maxRetries: true,
    retryDelay: true,
    retryDelayMultiplier: true,
    cacheCancelledQualityThreshold: true
})
const serviceType = computed(() => settingsStore.getSetting(SETTINGS.SERVICE_TYPE))

const providerTimeoutKeys: Record<string, keyof ISettings> = {
    libretranslate: SETTINGS.LIBRETRANSLATE_REQUEST_TIMEOUT,
    google: SETTINGS.GOOGLE_REQUEST_TIMEOUT,
    bing: SETTINGS.BING_REQUEST_TIMEOUT,
    microsoft: SETTINGS.MICROSOFT_REQUEST_TIMEOUT,
    yandex: SETTINGS.YANDEX_REQUEST_TIMEOUT,
    deepl: SETTINGS.DEEPL_REQUEST_TIMEOUT,
    openai: SETTINGS.OPENAI_REQUEST_TIMEOUT,
    anthropic: SETTINGS.ANTHROPIC_REQUEST_TIMEOUT,
    localai: SETTINGS.LOCALAI_REQUEST_TIMEOUT,
    gemini: SETTINGS.GEMINI_REQUEST_TIMEOUT,
    deepseek: SETTINGS.DEEPSEEK_REQUEST_TIMEOUT,
    openrouter: SETTINGS.OPENROUTER_REQUEST_TIMEOUT,
    zai: SETTINGS.ZAI_REQUEST_TIMEOUT,
    'opencode-go': SETTINGS.OPENCODE_GO_REQUEST_TIMEOUT,
    qwen: SETTINGS.QWEN_REQUEST_TIMEOUT,
    'qwen-mt': SETTINGS.QWEN_MT_REQUEST_TIMEOUT,
    xai: SETTINGS.XAI_REQUEST_TIMEOUT,
    'xai-oauth': SETTINGS.XAI_OAUTH_REQUEST_TIMEOUT,
    mistral: SETTINGS.MISTRAL_REQUEST_TIMEOUT
}

const activeProvider = computed((): string => {
    const raw = serviceType.value
    if (typeof raw !== 'string') return ''
    try {
        const parsed = JSON.parse(raw)
        if (Array.isArray(parsed) && typeof parsed[0]?.provider === 'string') {
            return parsed[0].provider.toLowerCase()
        }
    } catch {
        // Legacy plain provider value.
    }
    return raw.toLowerCase()
})

const batchProviders = new Set<string>([
    SERVICE_TYPE.ANTHROPIC,
    SERVICE_TYPE.DEEPSEEK,
    SERVICE_TYPE.GEMINI,
    SERVICE_TYPE.LOCALAI,
    SERVICE_TYPE.OPENAI,
    SERVICE_TYPE.OPENROUTER,
    SERVICE_TYPE.ZAI,
    SERVICE_TYPE.OPENCODE_GO,
    SERVICE_TYPE.QWEN,
    SERVICE_TYPE.XAI,
    SERVICE_TYPE.XAI_OAUTH,
    SERVICE_TYPE.MISTRAL
])
const supportsBatch = computed(() => batchProviders.has(activeProvider.value))

const requestTimeoutKey = computed(
    (): keyof ISettings =>
        providerTimeoutKeys[activeProvider.value] ?? SETTINGS.REQUEST_TIMEOUT
)

const requestTimeoutLabel = computed(() =>
    activeProvider.value
        ? `${activeProvider.value.replace('-', ' ')} request timeout`
        : 'Default request timeout'
)

const cacheCancelledProgress = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.CACHE_CANCELLED_PROGRESS) as string) || 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.CACHE_CANCELLED_PROGRESS, newValue, true)
        saveNotification.value?.show()
    }
})

const cacheCancelledQualityThreshold = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.CACHE_CANCELLED_QUALITY_THRESHOLD) as string) || '90',
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.CACHE_CANCELLED_QUALITY_THRESHOLD,
            newValue,
            isValid.cacheCancelledQualityThreshold
        )
        saveNotification.value?.show()
    }
})

const useBatchTranslation = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.USE_BATCH_TRANSLATION) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.USE_BATCH_TRANSLATION, newValue, true)
        saveNotification.value?.show()
    }
})

const maxBatchSize = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_BATCH_SIZE) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.MAX_BATCH_SIZE, newValue, isValid.maxBatchSize)
        saveNotification.value?.show()
    }
})

const requestTimeout = computed({
    get: (): string => settingsStore.getSetting(requestTimeoutKey.value) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(requestTimeoutKey.value, newValue, isValid.requestTimeout)
        saveNotification.value?.show()
    }
})

const maxRetries = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_RETRIES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.MAX_RETRIES, newValue, isValid.maxRetries)
        saveNotification.value?.show()
    }
})

const retryDelay = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.RETRY_DELAY) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.RETRY_DELAY, newValue, isValid.retryDelay)
        saveNotification.value?.show()
    }
})

const retryDelayMultiplier = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.RETRY_DELAY_MULTIPLIER) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.RETRY_DELAY_MULTIPLIER,
            newValue,
            isValid.retryDelayMultiplier
        )
        saveNotification.value?.show()
    }
})
</script>
