<template>
    <CardComponent title="Indexing">
        <template #description>
            The media indexing schedule controls the iteration with which Lingarr Next should sync with
            Sonarr and Radarr.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />
            <div class="flex flex-col space-y-2 pb-4">
                <span class="font-semibold">Set movie indexer:</span>
                <InputComponent
                    v-model="movieSchedule"
                    label="Cron format: minute hour day month weekday (e.g., '0 * * * *' for hourly)"
                    :placeholder="'0 * * * *'"
                    :validation-type="INPUT_VALIDATION_TYPE.CRON"
                    @update:validation="(val) => (movieScheduleIsValid = val)" />
                <span class="font-semibold">Set tv show indexer:</span>
                <InputComponent
                    v-model="showSchedule"
                    label="Cron format: minute hour day month weekday (e.g., '0 * * * *' for hourly)"
                    :placeholder="'0 * * * *'"
                    :validation-type="INPUT_VALIDATION_TYPE.CRON"
                    @update:validation="(val) => (showScheduleIsValid = val)" />
            </div>
        </template>
    </CardComponent>

    <CardComponent title="Library disk scan">
        <template #description>
            The daily translation check uses Plex, Radarr, and Sonarr. It skips
            an item that already has the source and target languages. It opens
            a folder only for a new import that is still missing a language.
            This switch is the full drive walk, used only when none of those
            three are connected. Leave it paused.
        </template>
        <template #content>
            <div class="flex items-center space-x-2">
                <span>Scan all folders on a schedule:</span>
                <ToggleButton v-model="libraryDiskScanEnabled">
                    <span class="text-primary-content text-sm font-medium">
                        {{ libraryDiskScanEnabled === 'true' ? 'On' : 'Paused' }}
                    </span>
                </ToggleButton>
            </div>
        </template>
    </CardComponent>

    <CardComponent title="Automation">
        <template #description>
            Set up automation. Note that if automation is implemented, you also need to configure
            the necessary
            <a
                class="cursor-pointer underline"
                @click="router.push({ name: 'translation-setup-settings' })">
                translation services
            </a>
            .
        </template>
        <template #content>
            <div class="flex flex-col space-y-4">
                <div class="flex items-center space-x-2">
                    <span>Automated translation:</span>
                    <ToggleButton v-model="automationEnabled">
                        <span class="text-primary-content text-sm font-medium">
                            {{ automationEnabled === 'true' ? 'Enabled' : 'Disabled' }}
                        </span>
                    </ToggleButton>
                </div>

                <span class="font-semibold">Set translation schedule:</span>
                <InputComponent
                    v-model="translationSchedule"
                    label="Once a day at 02:00 UTC. Cron: minute hour day month weekday."
                    :placeholder="'0 2 * * *'"
                    :validation-type="INPUT_VALIDATION_TYPE.CRON"
                    @update:validation="(val) => (translationScheduleIsValid = val)" />

                <span class="font-semibold">Limits:</span>
                <InputComponent
                    v-model="maxTranslationsPerRun"
                    :type="INPUT_TYPE.NUMBER"
                    :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                    :min-length="0"
                    label="Limit the amount of translations per schedule"
                    @update:validation="(val) => (maxTranslationsPerRunIsValid = val)" />

                <span class="font-semibold">Default file age delay for translation:</span>
                <InputComponent
                    v-model="movieAgeThreshold"
                    :type="INPUT_TYPE.NUMBER"
                    :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                    :min-length="0"
                    label="Movie file age delay in 'hours'"
                    @update:validation="(val) => (movieAgeThresholdIsValid = val)" />
                <InputComponent
                    v-model="showAgeThreshold"
                    :type="INPUT_TYPE.NUMBER"
                    :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                    :min-length="0"
                    label="TV Show file age delay in 'hours'"
                    @update:validation="(val) => (showAgeThresholdIsValid = val)" />
            </div>
        </template>
    </CardComponent>

    <CardComponent title="Library housekeeping">
        <template #description>
            When library disk scan is on, this job renames sidecar files and
            can extract an English subtitle from videos that have no English
            sidecar. Picture and caption tracks follow the switches on the
            Subtitles page. It stays idle while the scan is paused.
        </template>
        <template #content>
            <div class="flex flex-col space-y-4">
                <div class="flex items-center space-x-2">
                    <span>Standardize subtitle names:</span>
                    <ToggleButton v-model="subtitleNamingEnabled">
                        <span class="text-primary-content text-sm font-medium">
                            {{ subtitleNamingEnabled === 'true' ? 'Enabled' : 'Disabled' }}
                        </span>
                    </ToggleButton>
                </div>
                <div class="flex items-center space-x-2">
                    <span>Extract English from video when missing:</span>
                    <ToggleButton v-model="subtitleExtractEnabled">
                        <span class="text-primary-content text-sm font-medium">
                            {{ subtitleExtractEnabled === 'true' ? 'Enabled' : 'Disabled' }}
                        </span>
                    </ToggleButton>
                </div>
                <span class="font-semibold">Housekeeping schedule:</span>
                <InputComponent
                    v-model="subtitleMaintenanceSchedule"
                    label="Cron format. Default is Sunday 03:00 UTC."
                    :placeholder="'0 3 * * 0'"
                    :validation-type="INPUT_VALIDATION_TYPE.CRON"
                    @update:validation="(val) => (subtitleMaintenanceScheduleIsValid = val)" />
                <InputComponent
                    v-model="subtitleExtractMaxPerRun"
                    :type="INPUT_TYPE.NUMBER"
                    :validation-type="INPUT_VALIDATION_TYPE.NUMBER"
                    :min-length="0"
                    label="Maximum videos to extract per run"
                    @update:validation="(val) => (subtitleExtractMaxPerRunIsValid = val)" />
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useSettingStore } from '@/store/setting'
import { useRouter } from 'vue-router'
import { INPUT_TYPE, INPUT_VALIDATION_TYPE, SETTINGS } from '@/ts'
import CardComponent from '@/components/common/CardComponent.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const maxTranslationsPerRunIsValid = ref(false)
const movieAgeThresholdIsValid = ref(false)
const showAgeThresholdIsValid = ref(false)
const movieScheduleIsValid = ref(false)
const showScheduleIsValid = ref(false)
const translationScheduleIsValid = ref(false)
const subtitleMaintenanceScheduleIsValid = ref(false)
const subtitleExtractMaxPerRunIsValid = ref(false)
const settingsStore = useSettingStore()
const router = useRouter()

const libraryDiskScanEnabled = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.LIBRARY_DISK_SCAN_ENABLED) as string) || 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.LIBRARY_DISK_SCAN_ENABLED, newValue, true)
        saveNotification.value?.show()
    }
})
const automationEnabled = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.AUTOMATION_ENABLED) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.AUTOMATION_ENABLED, newValue, true)
        saveNotification.value?.show()
    }
})
const movieSchedule = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MOVIE_SCHEDULE) as string,
    set: (newValue: string): void => {
        if (movieScheduleIsValid.value) {
            settingsStore.updateSetting(SETTINGS.MOVIE_SCHEDULE, newValue, true)
            saveNotification.value?.show()
        }
    }
})
const showSchedule = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SHOW_SCHEDULE) as string,
    set: (newValue: string): void => {
        if (showScheduleIsValid.value) {
            settingsStore.updateSetting(SETTINGS.SHOW_SCHEDULE, newValue, true)
            saveNotification.value?.show()
        }
    }
})
const translationSchedule = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.TRANSLATION_SCHEDULE) as string,
    set: (newValue: string): void => {
        if (translationScheduleIsValid.value) {
            settingsStore.updateSetting(SETTINGS.TRANSLATION_SCHEDULE, newValue, true)
            saveNotification.value?.show()
        }
    }
})
const maxTranslationsPerRun = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_TRANSLATIONS_PER_RUN) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.MAX_TRANSLATIONS_PER_RUN, newValue, true)
        saveNotification.value?.show()
    }
})

const movieAgeThreshold = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MOVIE_AGE_THRESHOLD) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.MOVIE_AGE_THRESHOLD, newValue, true)
        saveNotification.value?.show()
    }
})

const subtitleNamingEnabled = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_NAMING_ENABLED) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_NAMING_ENABLED, newValue, true)
        saveNotification.value?.show()
    }
})
const subtitleExtractEnabled = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_EXTRACT_ENABLED) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_EXTRACT_ENABLED, newValue, true)
        saveNotification.value?.show()
    }
})
const subtitleMaintenanceSchedule = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_MAINTENANCE_SCHEDULE) as string,
    set: (newValue: string): void => {
        if (subtitleMaintenanceScheduleIsValid.value) {
            settingsStore.updateSetting(SETTINGS.SUBTITLE_MAINTENANCE_SCHEDULE, newValue, true)
            saveNotification.value?.show()
        }
    }
})
const subtitleExtractMaxPerRun = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_EXTRACT_MAX_PER_RUN) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_EXTRACT_MAX_PER_RUN, newValue, true)
        saveNotification.value?.show()
    }
})

const showAgeThreshold = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SHOW_AGE_THRESHOLD) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SHOW_AGE_THRESHOLD, newValue, true)
        saveNotification.value?.show()
    }
})
</script>
