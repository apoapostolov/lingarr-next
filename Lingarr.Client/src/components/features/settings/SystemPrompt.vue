<template>
    <CardComponent title="System Prompt Profiles">
        <template #description>
            Define reusable translation rules for tone, terminology, and formatting.
        </template>
        <template #content>
            <PromptProfileEditor
                type="system"
                title="System Prompt"
                :help="help"
                :recommended-example="RECOMMENDED_SYSTEM_PROMPT" />
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import CardComponent from '@/components/common/CardComponent.vue'
import PromptProfileEditor from '@/components/features/settings/PromptProfileEditor.vue'
import { RECOMMENDED_SYSTEM_PROMPT } from '@/components/features/settings/promptExamples'

const help =
    'Set translation rules for tone, terminology, names, and formatting. Publish a version to apply it to new requests.'
</script>
