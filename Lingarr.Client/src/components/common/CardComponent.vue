<template>
    <div class="bg-linear-to-br relative rounded-md from-secondary to-tertiary p-6 shadow-md">
        <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div class="min-w-0" :class="muted ? 'opacity-40' : ''">
                <h2 v-if="title" class="mb-2 text-2xl font-bold text-primary-content">
                    {{ title }}
                </h2>
                <p class="text-secondary-content/80 mb-6">
                    <slot name="description"></slot>
                </p>
            </div>
            <div class="shrink-0">
                <slot name="actions"></slot>
            </div>
        </div>

        <div class="space-y-4" :class="muted ? 'opacity-40' : ''">
            <slot name="content"></slot>
        </div>
    </div>
</template>

<script setup lang="ts">
const { title, muted = false } = defineProps<{
    title: string
    muted?: boolean
}>()
</script>
