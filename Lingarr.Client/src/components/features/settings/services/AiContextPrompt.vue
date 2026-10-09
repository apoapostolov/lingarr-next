<template>
    <TextAreaComponent
        v-model="aiContextPrompt"
        :rows="10"
        :min-height="100"
        :placeholders="[
            {
                placeholder: '{sourceLanguage}',
                placeholderText: 'Insert {sourceLanguage}',
                title: 'Source Language',
                description: 'Source subtitle language',
                required: true
            },
            {
                placeholder: '{targetLanguage}',
                placeholderText: 'Insert {targetLanguage}',
                title: 'Target Language',
                description: 'Target language',
                required: true
            },
            {
                placeholder: '{lineToTranslate}',
                placeholderText: 'Insert {lineToTranslate}',
                title: 'Subtitle line',
                description: 'Target subtitle line',
                required: false
            },
            {
                placeholder: '{contextBefore}',
                placeholderText: 'Insert {contextBefore}',
                title: 'Context',
                description: 'Subtitle lines preceding the target',
                required: false
            },
            {
                placeholder: '{contextAfter}',
                placeholderText: 'Insert {contextAfter}',
                title: 'Context',
                description: 'Subtitle lines following the target',
                required: false
            }
        ]"
        @update:validation="(val) => (isValid = val)" />
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import TextAreaComponent from '@/components/common/TextAreaComponent.vue'

const isValid = ref(false)
const settingsStore = useSettingStore()
const emit = defineEmits(['save'])

const aiContextPrompt = computed({
    get: () => settingsStore.getSetting(SETTINGS.AI_CONTEXT_PROMPT) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.AI_CONTEXT_PROMPT, newValue, isValid.value)
        if (isValid.value) {
            emit('save')
        }
    }
})
</script>
