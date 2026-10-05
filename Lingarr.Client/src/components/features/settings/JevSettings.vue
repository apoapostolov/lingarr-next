<template>
    <CardComponent title="Jev">
        <template #description>
            TypeSafe Jev is a cheap, instant classifier that keeps sound cues, credits, service errors, and refusals out of translation.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />
            <InputComponent
                v-model="apiKey"
                :validation-type="INPUT_VALIDATION_TYPE.STRING"
                :type="INPUT_TYPE.PASSWORD"
                label="TypeSafe API key"
                @update:validation="() => undefined" />
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
import ToggleButton from '@/components/common/ToggleButton.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()

const apiKey = computed({
    get: (): string =>
        settingsStore.getEncryptedSetting(ENCRYPTED_SETTINGS.TYPESAFE_API_KEY) ?? '',
    set: (newValue: string): void => {
        settingsStore.updateEncryptedSetting(ENCRYPTED_SETTINGS.TYPESAFE_API_KEY, newValue, true)
        saveNotification.value?.show()
    }
})

const skipNonDialogue = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.JEV_SKIP_NON_DIALOGUE) as string) || 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.JEV_SKIP_NON_DIALOGUE, newValue, true)
        saveNotification.value?.show()
    }
})

const rejectUntranslated = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.JEV_REJECT_UNTRANSLATED) as string) || 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.JEV_REJECT_UNTRANSLATED, newValue, true)
        saveNotification.value?.show()
    }
})
</script>
