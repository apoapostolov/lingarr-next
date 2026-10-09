<template>
    <p v-if="isLoading" class="text-sm opacity-60">Loading plugins.</p>
    <p v-else-if="loadError" class="text-sm text-red-500">{{ loadError }}</p>
    <p v-else-if="plugins.length === 0" class="text-sm opacity-60">
        No plugins loaded. Add provider DLLs to <CodeSnippet>PLUGINS_PATH</CodeSnippet> and restart Lingarr Next.
    </p>
    <CardComponent
        v-for="plugin in plugins"
        v-else
        :key="plugin.provider"
        :title="plugin.displayName"
        :muted="plugin.enabled !== true">
        <template #description>{{ plugin.description }}</template>
        <template #actions>
            <ToggleButton
                :model-value="plugin.enabled === true"
                aria-label="Enabled"
                @update:model-value="saveHost(plugin, $event === true || $event === 'true')" />
        </template>
        <template #content>
            <InputComponent
                label="Order"
                :type="INPUT_TYPE.NUMBER"
                :model-value="plugin.order ?? 100"
                @update:model-value="saveOrder(plugin, $event)" />
            <SelectComponent
                label="Failure policy"
                :options="policies"
                :selected="plugin.failurePolicy || 'skip'"
                :sort-options="false"
                @update:selected="savePolicy(plugin, $event)" />
            <p v-if="plugin.capabilities?.length" class="text-sm">
                {{ plugin.capabilities.join(', ') }}
            </p>
            <p v-if="plugin.sourceFile" class="text-sm">
                <CodeSnippet>{{ plugin.sourceFile }}</CodeSnippet>
            </p>
            <p v-if="notices[plugin.provider]" class="text-sm">
                {{ notices[plugin.provider] }}
            </p>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { INPUT_TYPE, IPluginSummary, ISelectOption } from '@/ts'
import services from '@/services'
import CardComponent from '@/components/common/CardComponent.vue'
import CodeSnippet from '@/components/common/CodeSnippet.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import { bumpPluginUi } from '@/composables/pluginUiTick'

const policies: ISelectOption[] = [
    { label: 'Continue after an error', value: 'skip' },
    { label: 'Stop this file, keep earlier edits', value: 'stop' },
    { label: 'Fail the translation, keep the old file', value: 'fail' }
]

const isLoading = ref(true)
const loadError = ref<string | null>(null)
const plugins = ref<IPluginSummary[]>([])
const notices = ref<Record<string, string>>({})

const persist = async (plugin: IPluginSummary) => {
    try {
        notices.value = {
            ...notices.value,
            [plugin.provider]: await services.plugin.saveHost(plugin.provider, {
                enabled: plugin.enabled === true,
                order: plugin.order ?? 100,
                failurePolicy: plugin.failurePolicy || 'skip'
            })
        }
        bumpPluginUi()
    } catch {
        notices.value = {
            ...notices.value,
            [plugin.provider]: 'Plugin settings could not be saved.'
        }
    }
}

const saveHost = (plugin: IPluginSummary, enabled: boolean) => {
    plugin.enabled = enabled
    void persist(plugin)
}

const saveOrder = (plugin: IPluginSummary, value: string | number) => {
    const order = Number(value)
    if (!Number.isFinite(order)) return
    plugin.order = order
    void persist(plugin)
}

const savePolicy = (plugin: IPluginSummary, value: string) => {
    plugin.failurePolicy = value
    void persist(plugin)
}

onMounted(async () => {
    try {
        const summaries = await services.plugin.list()
        plugins.value = summaries.filter((plugin) => !plugin.isBuiltIn)
    } catch (error) {
        console.error('Failed to load plugins', error)
        loadError.value = 'Plugins could not be loaded.'
    } finally {
        isLoading.value = false
    }
})
</script>
