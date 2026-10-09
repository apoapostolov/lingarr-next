<template>
    <div class="w-full">
        <SettingsSectionTabs v-if="knownSection" :section="knownSection" />
        <div
            class="grid grid-flow-row auto-rows-max grid-cols-1 items-start gap-4 p-4 md:grid-cols-2 xl:grid-cols-2 2xl:grid-cols-3">
            <div v-if="loading" class="text-sm opacity-60">Loading panels.</div>
            <div v-else-if="panels.length === 0" class="text-sm opacity-60">
                This tab has no panels.
            </div>
            <CardComponent
                v-for="panel in panels"
                v-else
                :key="`${panel.provider}:${panel.id}`"
                :title="panel.title"
                :muted="panel.enabled !== true">
                            <template #description>{{ panel.description }}</template>
                            <template #actions>
                                <ToggleButton
                                    :model-value="panel.enabled === true"
                                    aria-label="Enabled"
                                    @update:model-value="
                                        setEnabled(panel, $event === true || $event === 'true')
                                    " />
                            </template>
                            <template #content>
                                <div
                                    v-for="field in panel.fields"
                                    :key="field.key"
                                    class="flex flex-col space-y-2">
                                    <div
                                        v-if="field.type === 'Toggle'"
                                        class="flex items-center space-x-2">
                                        <span>{{ field.label }}:</span>
                                        <ToggleButton
                                            :model-value="valueOf(panel.provider, field.key) === 'true'"
                                            :disabled="panel.enabled !== true"
                                            @update:model-value="
                                                saveToggle(
                                                    panel,
                                                    field.key,
                                                    $event === true || $event === 'true'
                                                )
                                            ">
                                            <span class="text-primary-content text-sm font-medium">
                                                {{
                                                    valueOf(panel.provider, field.key) === 'true'
                                                        ? 'On'
                                                        : 'Off'
                                                }}
                                            </span>
                                        </ToggleButton>
                                    </div>
                                    <SelectComponent
                                        v-else-if="field.type === 'Dropdown'"
                                        :label="field.label"
                                        :options="field.options ?? []"
                                        :selected="valueOf(panel.provider, field.key)"
                                        :disabled="panel.enabled !== true"
                                        :sort-options="false"
                                        @update:selected="saveText(panel, field.key, String($event ?? ''))" />
                                    <InputComponent
                                        v-else
                                        :label="field.label"
                                        :type="
                                            field.type === 'Secret'
                                                ? INPUT_TYPE.PASSWORD
                                                : INPUT_TYPE.TEXT
                                        "
                                        :model-value="valueOf(panel.provider, field.key)"
                                        :disabled="panel.enabled !== true"
                                        @update:model-value="saveText(panel, field.key, String($event ?? ''))" />
                                    <p v-if="field.description" class="text-sm">
                                        {{ field.description }}
                                    </p>
                                </div>
                                <div v-if="panel.actions.length" class="flex flex-wrap gap-2">
                                    <ButtonComponent
                                        v-for="action in panel.actions"
                                        :key="action.id"
                                        type="button"
                                        :disabled="panel.enabled !== true"
                                        @click="run(panel.provider, panel.id, action.id)">
                                        {{ action.label }}
                                    </ButtonComponent>
                                </div>
                                <p v-if="noticeFor(panel)" class="text-sm">{{ noticeFor(panel) }}</p>
                            </template>
            </CardComponent>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import services from '@/services'
import { INPUT_TYPE, type IPluginUiPanel } from '@/ts'
import SettingsSectionTabs from '@/components/features/settings/SettingsSectionTabs.vue'
import CardComponent from '@/components/common/CardComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import { bumpPluginUi } from '@/composables/pluginUiTick'

const route = useRoute()
const loading = ref(true)
const panels = ref<IPluginUiPanel[]>([])
const values = ref<Record<string, Record<string, string>>>({})
const notices = ref<Record<string, string>>({})

const knownSection = computed(() => {
    const section = String(route.params.section || '')
    if (
        section === 'connections' ||
        section === 'translation' ||
        section === 'system' ||
        section === 'plugins'
    ) {
        return section
    }
    return null
})

const noticeKey = (provider: string, panelId: string) => `${provider}:${panelId}`

const noticeFor = (panel: IPluginUiPanel) => notices.value[noticeKey(panel.provider, panel.id)] || ''

const valueOf = (provider: string, key: string) => values.value[provider]?.[key] ?? ''

const load = async () => {
    loading.value = true
    notices.value = {}
    try {
        const ui = await services.plugin.ui()
        const match = ui.panels
            .filter(
                (item) => item.section === route.params.section && item.tabId === route.params.tabId
            )
            .slice()
            .sort(
                (left, right) =>
                    (left.order ?? 100) - (right.order ?? 100) || left.title.localeCompare(right.title)
            )
        panels.value = match
        const next: Record<string, Record<string, string>> = {}
        for (const provider of [...new Set(match.map((item) => item.provider))]) {
            next[provider] = await services.plugin.values(provider)
        }
        values.value = next
    } catch {
        panels.value = []
    } finally {
        loading.value = false
    }
}

const setEnabled = async (panel: IPluginUiPanel, enabled: boolean) => {
    const previous = panel.enabled === true
    for (const item of panels.value) {
        if (item.provider === panel.provider) item.enabled = enabled
    }
    try {
        await services.plugin.saveHost(panel.provider, {
            enabled,
            order: panel.order ?? 100,
            failurePolicy: panel.failurePolicy || 'skip'
        })
        bumpPluginUi()
    } catch {
        for (const item of panels.value) {
            if (item.provider === panel.provider) item.enabled = previous
        }
        notices.value = {
            ...notices.value,
            [noticeKey(panel.provider, panel.id)]: 'Plugin settings could not be saved.'
        }
    }
}

const saveToggle = async (panel: IPluginUiPanel, key: string, enabled: boolean) => {
    const next = enabled ? 'true' : 'false'
    values.value = {
        ...values.value,
        [panel.provider]: { ...(values.value[panel.provider] ?? {}), [key]: next }
    }
    try {
        await services.plugin.saveValues(panel.provider, { [key]: next })
    } catch {
        notices.value = {
            ...notices.value,
            [noticeKey(panel.provider, panel.id)]: 'Plugin settings could not be saved.'
        }
    }
}

const saveText = async (panel: IPluginUiPanel, key: string, text: string) => {
    values.value = {
        ...values.value,
        [panel.provider]: { ...(values.value[panel.provider] ?? {}), [key]: text }
    }
    try {
        await services.plugin.saveValues(panel.provider, { [key]: text })
    } catch {
        notices.value = {
            ...notices.value,
            [noticeKey(panel.provider, panel.id)]: 'Plugin settings could not be saved.'
        }
    }
}

const run = async (provider: string, panelId: string, action: string) => {
    const key = noticeKey(provider, panelId)
    notices.value = { ...notices.value, [key]: '' }
    try {
        notices.value = {
            ...notices.value,
            [key]: await services.plugin.runAction(provider, action)
        }
    } catch (error: unknown) {
        const response = error as { data?: { message?: string } }
        notices.value = {
            ...notices.value,
            [key]: response?.data?.message || 'The action could not be run.'
        }
    }
}

watch(() => route.fullPath, load, { immediate: true })
</script>
