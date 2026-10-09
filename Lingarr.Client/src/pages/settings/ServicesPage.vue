<template>
    <div class="w-full">
        <SettingsSectionTabs section="translation" />
        <ServicesSettings />
    </div>
</template>

<script setup lang="ts">
import ServicesSettings from '@/components/features/settings/ServicesSettings.vue'
import SettingsSectionTabs from '@/components/features/settings/SettingsSectionTabs.vue'
</script>
