<template>
    <div class="w-full">
        <SettingsSectionTabs section="translation" />
        <div
            class="grid grid-flow-row auto-rows-max grid-cols-1 gap-4 p-4 md:grid-cols-2 xl:grid-cols-2 2xl:grid-cols-3">
            <TranslationSettings />
            <JevSettings />
            <CardComponent title="Request Templates">
                <template #description>
                    Customize the request body sent to each configured AI translation provider.
                </template>
                <template #content>
                    <p class="text-secondary-content text-sm">
                        Request templates are available for configured providers that support custom
                        request bodies.
                    </p>
                    <ButtonComponent
                        variant="secondary"
                        :disabled="!firstTemplateProvider"
                        @click="openRequestTemplates">
                        Configure request templates
                    </ButtonComponent>
                    <p v-if="!firstTemplateProvider" class="text-secondary-content/60 text-xs">
                        Add a compatible AI provider in Translation Setup first.
                    </p>
                </template>
            </CardComponent>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { IPluginSummary, SETTINGS } from '@/ts'
import { useSettingStore } from '@/store/setting'
import services from '@/services'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import CardComponent from '@/components/common/CardComponent.vue'
import SettingsSectionTabs from '@/components/features/settings/SettingsSectionTabs.vue'
import TranslationSettings from '@/components/features/settings/TranslationSettings.vue'
import JevSettings from '@/components/features/settings/JevSettings.vue'

const router = useRouter()
const settingsStore = useSettingStore()
const plugins = ref<IPluginSummary[]>([])

const configuredProviders = computed(() => {
    const raw = String(settingsStore.getSetting(SETTINGS.SERVICE_TYPE) ?? '')
    if (!raw.trim()) return []

    try {
        if (!raw.trim().startsWith('[')) return [raw.trim()]

        const parsed = JSON.parse(raw) as unknown[]
        return parsed
            .map((entry) => {
                if (typeof entry === 'string') return entry
                if (entry && typeof entry === 'object') {
                    const item = entry as { provider?: string; service?: string }
                    return item.provider || item.service || ''
                }
                return ''
            })
            .filter(Boolean)
    } catch {
        return []
    }
})

const firstTemplateProvider = computed(() => {
    const supported = new Set(
        plugins.value.filter((plugin) => plugin.hasRequestTemplate).map((plugin) => plugin.provider)
    )
    return configuredProviders.value.find((provider) => supported.has(provider)) ?? ''
})

function openRequestTemplates() {
    if (!firstTemplateProvider.value) return
    router.push({
        name: 'request-template-settings',
        params: { service: firstTemplateProvider.value }
    })
}

onMounted(async () => {
    try {
        plugins.value = await services.plugin.list()
    } catch (error) {
        console.error('Failed to load translation provider list', error)
    }
})
</script>
