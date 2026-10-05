<template>
    <CardComponent title="Bazarr">
        <template #description>
            When a movie or episode has no source subtitle, Lingarr asks Bazarr to download the highest scored match in the source language, then starts the translation.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />
            <div class="flex flex-col">
                <span class="font-semibold">Fetch a missing source subtitle</span>
                Off until you turn it on. Bazarr must already know the movie or episode through Radarr or Sonarr.
            </div>
            <ToggleButton v-model="enabled">
                <span class="text-sm font-medium text-primary-content">
                    {{ enabled == 'true' ? 'Enabled' : 'Disabled' }}
                </span>
            </ToggleButton>
            <div class="flex flex-col">
                <span class="font-semibold">Extract from the video first</span>
                On by default. Lingarr pulls an English track out of the video. It asks Bazarr only if that subtitle is still missing. Turn this off to ask Bazarr right away.
            </div>
            <ToggleButton v-model="extractFirst">
                <span class="text-sm font-medium text-primary-content">
                    {{ extractFirst == 'true' ? 'Enabled' : 'Disabled' }}
                </span>
            </ToggleButton>
            <InputComponent
                v-model="url"
                :validation-type="INPUT_VALIDATION_TYPE.URL"
                label="Address"
                error-message="Please enter a valid URL (e.g., http://bazarr:6767)"
                @update:validation="(val) => (isValid.url = val)" />
            <InputComponent
                v-model="apiKey"
                :validation-type="INPUT_VALIDATION_TYPE.STRING"
                :type="INPUT_TYPE.PASSWORD"
                label="API key"
                @update:validation="() => undefined" />
            <div class="flex flex-col">
                <span class="font-semibold">Minimum score</span>
                Bazarr scores a match from 0 to 100. Lingarr downloads the highest score in the source language only when it reaches this number. Forced subtitles are skipped.
            </div>
            <InputComponent
                v-model="minimumScore"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                @update:validation="(val) => (isValid.minimumScore = val)" />
            <div class="flex flex-col">
                <span class="font-semibold">Replace OCR subtitles</span>
                On by default. When the only source subtitle was made by OCR, Lingarr keeps asking
                Bazarr. A downloaded subtitle replaces that OCR file.
            </div>
            <ToggleButton v-model="replaceOcr" aria-label="Replace OCR subtitles">
                <span class="text-sm font-medium text-primary-content">
                    {{ replaceOcr == 'true' ? 'Enabled' : 'Disabled' }}
                </span>
            </ToggleButton>
            <div class="flex flex-col">
                <span class="font-semibold">Retry when nothing is found</span>
                If Bazarr does not return a subtitle, Lingarr searches again after this many hours.
                The default is 12. Use 0 to search only once.
            </div>
            <InputComponent
                v-model="retryHours"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                label="Retry hours"
                @update:validation="(val) => (isValid.retryHours = val)" />
            <div class="flex flex-col">
                <span class="font-semibold">Stop searching</span>
                Retries stop this many hours after the file is added. The default is 168, which is 7 days.
            </div>
            <InputComponent
                v-model="timeoutHours"
                :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                label="Stop after hours"
                @update:validation="(val) => (isValid.timeoutHours = val)" />
            <button
                class="cursor-pointer rounded border border-accent px-3 py-2 text-sm transition-colors hover:bg-accent hover:text-white"
                type="button"
                :disabled="testing"
                @click="test">
                {{ testing ? 'Testing...' : 'Test' }}
            </button>
            <p v-if="notice" class="text-sm text-secondary-content">{{ notice }}</p>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import axios from 'axios'
import { useSettingStore } from '@/store/setting'
import { ENCRYPTED_SETTINGS, INPUT_TYPE, INPUT_VALIDATION_TYPE, SETTINGS } from '@/ts'
import CardComponent from '@/components/common/CardComponent.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const testing = ref(false)
const notice = ref('')
const isValid = reactive({ url: true, minimumScore: true, retryHours: true, timeoutHours: true })

const extractFirst = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.BAZARR_EXTRACT_FIRST) as string) || 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.BAZARR_EXTRACT_FIRST, newValue, true)
        saveNotification.value?.show()
    }
})

const enabled = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.BAZARR_ENABLED) as string) || 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.BAZARR_ENABLED, newValue, true)
        saveNotification.value?.show()
    }
})

const url = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.BAZARR_URL) as string) || '',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.BAZARR_URL, newValue, isValid.url)
        saveNotification.value?.show()
    }
})

const apiKey = computed({
    get: (): string => settingsStore.getEncryptedSetting(ENCRYPTED_SETTINGS.BAZARR_API_KEY) ?? '',
    set: (newValue: string): void => {
        settingsStore.updateEncryptedSetting(ENCRYPTED_SETTINGS.BAZARR_API_KEY, newValue, true)
        saveNotification.value?.show()
    }
})

const minimumScore = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.BAZARR_MINIMUM_SCORE) as string) || '70',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.BAZARR_MINIMUM_SCORE, newValue, isValid.minimumScore)
        saveNotification.value?.show()
    }
})

const retryHours = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.BAZARR_RETRY_HOURS) as string) || '12',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.BAZARR_RETRY_HOURS, newValue, isValid.retryHours)
        saveNotification.value?.show()
    }
})

const replaceOcr = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.BAZARR_REPLACE_OCR) as string) || 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.BAZARR_REPLACE_OCR, newValue, true)
        saveNotification.value?.show()
    }
})

const timeoutHours = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.BAZARR_RETRY_TIMEOUT_HOURS) as string) || '168',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.BAZARR_RETRY_TIMEOUT_HOURS, newValue, isValid.timeoutHours)
        saveNotification.value?.show()
    }
})

async function test() {
    testing.value = true
    notice.value = ''
    try {
        const { data } = await axios.post<{ ok: boolean; message: string }>('/api/bazarr/test')
        notice.value = data.message || (data.ok ? 'Bazarr connection succeeded.' : 'Bazarr connection failed.')
    } catch {
        notice.value = 'Bazarr connection failed.'
    } finally {
        testing.value = false
    }
}
</script>
