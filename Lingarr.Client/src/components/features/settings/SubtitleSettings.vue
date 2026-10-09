<template>
    <CardComponent title="Subtitle Processing">
        <template #description>
            Configure subtitle timing, formatting, and output filenames.
        </template>
        <template #content>
            <div class="flex flex-col space-y-4">
                <SaveNotification ref="saveNotification" />
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Skip translation when the target has SDH or forced captions</span>
                </div>
                <ToggleButton v-model="ignoreCaptions">
                    <span class="text-primary-content text-sm font-medium">
                        {{ ignoreCaptions == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Fix overlapping cues</span>
                    Corrects overlapping cue end times. Use with synchronized source files.
                </div>
                <ToggleButton v-model="fixOverlappingSubtitles">
                    <span class="text-primary-content text-sm font-medium">
                        {{ fixOverlappingSubtitles == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Strip HTML</span>
                    Removes HTML tags from subtitles before translation.
                </div>
                <ToggleButton v-model="stripSubtitleHtml">
                    <span class="text-primary-content text-sm font-medium">
                        {{ stripSubtitleHtml == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Strip subtitle formatting</span>
                    Removes styling tags from SRT files before translation.
                </div>
                <ToggleButton v-model="stripSubtitleFormatting">
                    <span class="text-primary-content text-sm font-medium">
                        {{ stripSubtitleFormatting == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Preserve line breaks</span>
                </div>
                <ToggleButton v-model="preserveLineBreaks">
                    <span class="text-primary-content text-sm font-medium">
                        {{ preserveLineBreaks == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Add translator attribution</span>
                </div>
                <ToggleButton v-model="addTranslatorInfo">
                    <span class="text-primary-content text-sm font-medium">
                        {{ addTranslatorInfo == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Remove source language tag</span>
                    Removes source language codes such as <code>.en</code> from filenames.
                </div>
                <ToggleButton v-model="removeLanguageTag">
                    <span class="text-primary-content text-sm font-medium">
                        {{ removeLanguageTag == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-y-4">
                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">Add a subtitle filename tag</span>
                        Adds a custom filename segment; support varies by media software.
                    </div>
                    <ToggleButton v-model="useSubtitleTagging">
                        <span class="text-primary-content text-sm font-medium">
                            {{ useSubtitleTagging == 'true' ? 'Enabled' : 'Disabled' }}
                        </span>
                    </ToggleButton>
                    <InputComponent
                        v-if="useSubtitleTagging == 'true'"
                        v-model="subtitleTag"
                        :validation-type="INPUT_VALIDATION_TYPE.STRING"
                        label="Subtitle tag"
                        @update:validation="(val) => (isValid.subtitleTag = val)" />
                </div>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { ref, computed, reactive } from 'vue'
import { INPUT_VALIDATION_TYPE, SETTINGS } from '@/ts'
import { useSettingStore } from '@/store/setting'

import CardComponent from '@/components/common/CardComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import InputComponent from '@/components/common/InputComponent.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const isValid = reactive({
    subtitleTag: true
})

const ignoreCaptions = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.IGNORE_CAPTIONS) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.IGNORE_CAPTIONS, newValue, true)
        saveNotification.value?.show()
    }
})

const fixOverlappingSubtitles = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.FIX_OVERLAPPING_SUBTITLES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.FIX_OVERLAPPING_SUBTITLES, newValue, true)
        saveNotification.value?.show()
    }
})

const stripSubtitleHtml = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.STRIP_SUBTITLE_HTML) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.STRIP_SUBTITLE_HTML, newValue, true)
        saveNotification.value?.show()
    }
})

const stripSubtitleFormatting = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.STRIP_SUBTITLE_FORMATTING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.STRIP_SUBTITLE_FORMATTING, newValue, true)
        saveNotification.value?.show()
    }
})

const preserveLineBreaks = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.PRESERVE_LINE_BREAKS) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.PRESERVE_LINE_BREAKS, newValue, true)
        saveNotification.value?.show()
    }
})

const addTranslatorInfo = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.ADD_TRANSLATOR_INFO) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.ADD_TRANSLATOR_INFO, newValue, true)
        saveNotification.value?.show()
    }
})

const removeLanguageTag = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.REMOVE_LANGUAGE_TAG) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.REMOVE_LANGUAGE_TAG, newValue, true)
        saveNotification.value?.show()
    }
})

const useSubtitleTagging = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.USE_SUBTITLE_TAGGING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.USE_SUBTITLE_TAGGING, newValue, true)
        saveNotification.value?.show()
    }
})

const subtitleTag = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_TAG) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_TAG, newValue, isValid.subtitleTag)
        saveNotification.value?.show()
    }
})
</script>
