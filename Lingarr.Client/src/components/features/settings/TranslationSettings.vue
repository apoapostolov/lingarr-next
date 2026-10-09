<template>
    <CardComponent title="Translation Request">
        <template #description>
            Configure batching, request timeouts, and retry behavior.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />

            <template v-if="supportsBatch">
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Use batch translation</span>
                    Sends multiple subtitle lines in one request. Batch behavior depends on the provider.
                </div>
                <ToggleButton v-model="useBatchTranslation">
                    <span class="text-sm font-medium text-primary-content">
                        {{ useBatchTranslation == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>
            </template>

            <template v-if="useBatchTranslation == 'true'">
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Batch size (subtitle lines)</span>
                </div>
                <InputComponent
                    v-model="maxBatchSize"
                    :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                    @update:validation="(val) => (isValid.maxBatchSize = val)" />
            </template>

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Reuse completed lines from cancelled requests</span>
                Retries resume lines that meet the threshold; lower scores restart the translation.
            </div>
            <ToggleButton v-model="cacheCancelledProgress">
                <span class="text-sm font-medium text-primary-content">
                    {{ cacheCancelledProgress == 'true' ? 'Enabled' : 'Disabled' }}
                </span>
            </ToggleButton>

            <template v-if="cacheCancelledProgress == 'true'">
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Minimum quality score</span>
                    Requests below this score restart from the beginning.
                </div>
                <InputComponent
                    v-model="cacheCancelledQualityThreshold"
                    :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                    @update:validation="(val) => (isValid.cacheCancelledQualityThreshold = val)" />
            </template>

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">{{ requestTimeoutLabel }} (minutes):</span>
            </div>
            <InputComponent
                v-model="requestTimeout"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.requestTimeout = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Job retries after provider cancellation</span>
            </div>
            <InputComponent
                v-model="providerCancelRetryCount"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.providerCancelRetryCount = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Delay before a full retry (hours)</span>
            </div>
            <InputComponent
                v-model="providerCancelRetryHours"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.providerCancelRetryHours = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Maximum line retries</span>
                Retries busy or timed-out lines within the current job.
            </div>
            <InputComponent
                v-model="maxRetries"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.maxRetries = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Initial line retry delay (seconds)</span>
            </div>
            <InputComponent
                v-model="retryDelay"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.retryDelay = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">Line retry growth multiplier</span>
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
    providerCancelRetryCount: true,
    providerCancelRetryHours: true,
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

const providerCancelRetryCount = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.PROVIDER_CANCEL_RETRY_COUNT) as string) || '5',
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.PROVIDER_CANCEL_RETRY_COUNT,
            newValue,
            isValid.providerCancelRetryCount
        )
        saveNotification.value?.show()
    }
})

const providerCancelRetryHours = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.PROVIDER_CANCEL_RETRY_HOURS) as string) || '2',
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.PROVIDER_CANCEL_RETRY_HOURS,
            newValue,
            isValid.providerCancelRetryHours
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
