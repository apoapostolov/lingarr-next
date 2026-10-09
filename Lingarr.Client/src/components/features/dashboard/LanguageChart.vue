<template>
    <div class="relative h-full w-full">
        <Bar
            v-if="chartData"
            :key="key"
            :data="chartData"
            :options="chartOptions"
            class="h-full w-full" />
        <div v-else class="flex h-full w-full items-center justify-center text-primary-content">
            No data available
        </div>
        <div
            v-show="hover.visible"
            class="border-accent bg-primary text-primary-content pointer-events-none absolute z-20 rounded-md border px-3 py-2 text-xs shadow-md"
            :style="{ left: hover.left, top: hover.top }">
            <p>{{ hover.date }}: <span class="font-bold">{{ hover.count }}</span></p>
            <p>Average: <span class="font-bold">{{ hover.average }}</span></p>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { Chart, ChartData, ChartOptions, TooltipModel } from 'chart.js'
import {
    Chart as ChartJS,
    ChartDataset,
    CategoryScale,
    LinearScale,
    BarElement,
    PointElement,
    LineElement,
    Title,
    Tooltip,
    Legend
} from 'chart.js/auto'
import { Bar } from 'vue-chartjs'
import { DailyStatistic } from '@/ts'
import { useInstanceStore } from '@/store/instance'

ChartJS.register(
    CategoryScale,
    LinearScale,
    BarElement,
    PointElement,
    LineElement,
    Title,
    Tooltip,
    Legend
)

const props = defineProps<{
    dailyStats: DailyStatistic[]
}>()

const getCssVariable = (variableName: string): string => {
    return getComputedStyle(document.documentElement).getPropertyValue(variableName).trim()
}

const colors = computed(() => {
    return {
        key: key.value, // force reload
        bar: {
            background: getCssVariable('--accent') + '80', // added opacity
            border: getCssVariable('--primary')
        },
        line: {
            border: getCssVariable('--accent-content')
        },
        card: {
            background: getCssVariable('--primary'),
            border: getCssVariable('--accent')
        }
    }
})
const instanceStore = useInstanceStore()
const key = ref(0)
watch(
    () => instanceStore.getTheme,
    async () => {
        key.value++
    }
)

const calculateMovingAverage = (data: number[], windowSize: number): number[] => {
    return data.map((_, index) => {
        const start = Math.max(0, index - windowSize + 1)
        const slice = data.slice(start, index + 1)
        return Math.round(slice.reduce((sum, val) => sum + val, 0) / slice.length)
    })
}

const dates = computed(() =>
    props.dailyStats.map((stat) =>
        new Date(stat.date).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
    )
)

const translationCounts = computed(() => props.dailyStats.map((stat) => stat.translationCount))
const movingAverage = computed(() => calculateMovingAverage(translationCounts.value, 7))

const chartData = computed<ChartData<'bar'> | undefined>(() => {
    if (!props.dailyStats.length) return undefined

    const datasets: ChartDataset<'bar' | 'line'>[] = [
        {
            label: 'Daily Translations',
            data: translationCounts.value,
            backgroundColor: colors.value.bar.background,
            borderColor: colors.value.bar.border,
            barThickness: 6,
            borderRadius: 4,
            maxBarThickness: 12,
            order: 2
        },
        {
            label: '7-Day Average',
            data: movingAverage.value,
            borderColor: colors.value.line.border,
            borderWidth: 2,
            pointRadius: 0,
            fill: false,
            tension: 0.4,
            type: 'line',
            order: 1
        }
    ]

    return {
        labels: dates.value,
        datasets
    } as ChartData<'bar'>
})

const hover = ref({
    visible: false,
    left: '0px',
    top: '0px',
    date: '',
    count: 0,
    average: 0
})

const showHover = (context: { chart: Chart; tooltip: TooltipModel<'bar'> }) => {
    const { chart, tooltip } = context
    if (tooltip.opacity === 0 || tooltip.dataPoints.length === 0) {
        hover.value.visible = false
        return
    }

    const daily = tooltip.dataPoints.find((point) => point.datasetIndex === 0)
    const average = tooltip.dataPoints.find((point) => point.datasetIndex === 1)
    const parent = chart.canvas.parentElement
    const width = parent?.clientWidth ?? chart.canvas.clientWidth
    const boxWidth = 160
    const rawLeft = tooltip.caretX - boxWidth / 2
    const left = Math.min(Math.max(rawLeft, 8), Math.max(8, width - boxWidth - 8))

    hover.value = {
        visible: true,
        left: `${left}px`,
        top: `${Math.max(tooltip.caretY - 52, 4)}px`,
        date: daily?.label ?? tooltip.title?.[0] ?? '',
        count: Number(daily?.parsed.y ?? 0),
        average: Number(average?.parsed.y ?? 0)
    }
}

const chartOptions: ChartOptions<'bar'> = {
    responsive: true,
    maintainAspectRatio: false,
    layout: {
        padding: {
            top: 20
        }
    },
    scales: {
        y: {
            beginAtZero: true,
            grid: {
                color: '#466e8c20'
            },
            ticks: {
                color: '#c0c8d2',
                font: {
                    size: 11
                },
                padding: 8,
                maxTicksLimit: 5
            },
            border: {
                display: false
            }
        },
        x: {
            grid: {
                display: false
            },
            ticks: {
                color: '#c0c8d2',
                font: {
                    size: 11
                },
                maxRotation: 45,
                minRotation: 45,
                padding: 8,
                maxTicksLimit: 10
            },
            border: {
                display: false
            }
        }
    },
    plugins: {
        legend: {
            position: 'top' as const,
            labels: {
                color: '#c0c8d2',
                font: {
                    size: 12
                },
                padding: 20,
                usePointStyle: true,
                pointStyle: 'circle'
            }
        },
        tooltip: {
            enabled: false,
            external: showHover
        }
    },
    interaction: {
        intersect: false,
        mode: 'index'
    }
}
</script>
