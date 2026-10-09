<template>
    <div class="w-full">
        <SettingsSectionTabs section="plugins" />
        <div
            class="grid grid-flow-row auto-rows-max grid-cols-1 items-start gap-4 p-4 md:grid-cols-2 xl:grid-cols-2 2xl:grid-cols-3">
            <PluginsSettings />
        </div>
    </div>
</template>

<script setup lang="ts">
import SettingsSectionTabs from '@/components/features/settings/SettingsSectionTabs.vue'
import PluginsSettings from '@/components/features/settings/PluginsSettings.vue'
</script>
