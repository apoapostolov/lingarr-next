<template>
    <div class="w-full">
        <SettingsSectionTabs section="translation" />
        <div class="grid grid-flow-row auto-rows-max grid-cols-1 gap-4 p-4">
            <LanguageSettings />
            <ServicesSettings />
            <ProviderHealthPanel />
        </div>
    </div>
</template>

<script setup lang="ts">
import LanguageSettings from '@/components/features/settings/LanguageSettings.vue'
import ServicesSettings from '@/components/features/settings/ServicesSettings.vue'
import SettingsSectionTabs from '@/components/features/settings/SettingsSectionTabs.vue'
import ProviderHealthPanel from '@/components/features/providerHealth/ProviderHealthPanel.vue'
</script>
