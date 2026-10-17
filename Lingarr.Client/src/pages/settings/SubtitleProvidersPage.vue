<template>
    <div class="w-full">
        <SettingsSectionTabs section="translation" />
        <div class="grid grid-flow-row auto-rows-max grid-cols-1 gap-4 p-4 md:grid-cols-2">
            <SubtitleDownloadSettings class="md:col-span-2" />
        </div>
    </div>
</template>

<script setup lang="ts">
import SettingsSectionTabs from '@/components/features/settings/SettingsSectionTabs.vue'
import SubtitleDownloadSettings from '@/components/features/settings/SubtitleDownloadSettings.vue'
</script>
