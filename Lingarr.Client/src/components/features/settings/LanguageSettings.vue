<template>
    <CardComponent title="Languages">
        <template #description>
            Set the source language and translation target.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />
            <SourceAndTarget @save="saveNotification?.show()" />
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import CardComponent from '@/components/common/CardComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import SourceAndTarget from '@/components/features/settings/SourceAndTarget.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
</script>
