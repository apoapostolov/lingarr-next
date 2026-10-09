<template>
    <CardComponent title="Classifier">
        <template #description>
            Classify subtitle lines before and after translation. The selected provider applies to both checks.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />
            <SelectComponent
                label="Classifier provider"
                :selected="provider"
                :options="providerOptions"
                @update:selected="provider = $event" />
            <template v-if="provider === 'jev'">
                <InputComponent
                    v-model="jevApiKey"
                    :validation-type="INPUT_VALIDATION_TYPE.STRING"
                    :type="INPUT_TYPE.PASSWORD"
                    label="TypeSafe API key"
                    @update:validation="() => undefined" />
            </template>
            <template v-else-if="provider === 'luna'">
                <InputComponent
                    v-model="openAiApiKey"
                    :validation-type="INPUT_VALIDATION_TYPE.STRING"
                    :type="INPUT_TYPE.PASSWORD"
                    label="OpenAI API key"
                    @update:validation="() => undefined" />
                <p class="text-secondary-content/70 text-xs">
                    Shared with OpenAI translation. An existing key is used here automatically; changing it updates both.
                </p>
            </template>
            <div class="flex flex-col">
                <span class="font-semibold">Skip sound cues and credits</span>
                Lines such as [door slams] or a credit roll stay as they are and are not sent to the translator.
            </div>
            <ToggleButton v-model="skipNonDialogue">
                <span class="text-sm font-medium text-primary-content">
                    {{ skipNonDialogue == 'true' ? 'Enabled' : 'Disabled' }}
                </span>
            </ToggleButton>
            <div class="flex flex-col">
                <span class="font-semibold">Drop a result that is not a translation</span>
                A refusal or a copy of the source is not kept. The original line stays.
            </div>
            <ToggleButton v-model="rejectUntranslated">
                <span class="text-sm font-medium text-primary-content">
                    {{ rejectUntranslated == 'true' ? 'Enabled' : 'Disabled' }}
                </span>
            </ToggleButton>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useSettingStore } from '@/store/setting'
import { ENCRYPTED_SETTINGS, INPUT_TYPE, INPUT_VALIDATION_TYPE, SETTINGS } from '@/ts'
import CardComponent from '@/components/common/CardComponent.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const providerOptions = [
    { value: 'jev', label: 'TypeSafe Jev' },
    { value: 'luna', label: 'GPT-6 Luna Decisions' }
]

const provider = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.CLASSIFIER_PROVIDER) as string) || 'jev',
    set: (value: string): void => {
        settingsStore.updateSetting(SETTINGS.CLASSIFIER_PROVIDER, value, true)
        saveNotification.value?.show()
    }
})

const jevApiKey = computed({
    get: (): string => settingsStore.getEncryptedSetting(ENCRYPTED_SETTINGS.TYPESAFE_API_KEY) ?? '',
    set: (value: string): void => {
        settingsStore.updateEncryptedSetting(ENCRYPTED_SETTINGS.TYPESAFE_API_KEY, value, true)
        saveNotification.value?.show()
    }
})

const openAiApiKey = computed({
    get: (): string => settingsStore.getEncryptedSetting(ENCRYPTED_SETTINGS.OPENAI_API_KEY) ?? '',
    set: (value: string): void => {
        settingsStore.updateEncryptedSetting(ENCRYPTED_SETTINGS.OPENAI_API_KEY, value, true)
        saveNotification.value?.show()
    }
})

const skipNonDialogue = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.JEV_SKIP_NON_DIALOGUE) as string) || 'false',
    set: (value: string): void => {
        settingsStore.updateSetting(SETTINGS.JEV_SKIP_NON_DIALOGUE, value, true)
        saveNotification.value?.show()
    }
})

const rejectUntranslated = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.JEV_REJECT_UNTRANSLATED) as string) || 'false',
    set: (value: string): void => {
        settingsStore.updateSetting(SETTINGS.JEV_REJECT_UNTRANSLATED, value, true)
        saveNotification.value?.show()
    }
})
</script>
