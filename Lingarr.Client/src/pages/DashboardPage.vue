<template>
    <div class="p-4">
        <div v-if="widgets.length" class="mb-4 grid grid-cols-1 gap-4 lg:grid-cols-2">
            <article
                v-for="widget in widgets"
                :key="widget.title"
                class="rounded-md bg-linear-to-br from-secondary to-tertiary p-6 shadow-md">
                <h2 class="text-2xl font-bold text-primary-content">{{ widget.title }}</h2>
                <p class="mt-2 text-sm text-primary-content">{{ widget.text }}</p>
            </article>
        </div>
        <StatisticsComponent />
    </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import axios from 'axios'
import StatisticsComponent from '@/components/features/dashboard/StatisticsComponent.vue'

interface PluginWidget {
    title: string
    text: string
}

const widgets = ref<PluginWidget[]>([])

onMounted(async () => {
    try {
        const { data } = await axios.get<PluginWidget[]>('/api/plugin/widgets')
        widgets.value = Array.isArray(data)
            ? data.filter((item) => item?.title && item?.text)
            : []
    } catch {
        widgets.value = []
    }
})
</script>
