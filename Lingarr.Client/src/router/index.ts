import { createRouter, createWebHistory, RouteRecordRaw } from 'vue-router'
import { useInstanceStore } from '@/store/instance'
import { baseUrl } from '@/utils/baseUrl'

const routes: RouteRecordRaw[] = [
    // Auth
    {
        path: '/auth',
        component: () => import('@/components/layout/AuthLayout.vue'),
        children: [
            {
                path: '/auth/onboarding',
                component: () => import('@/pages/OnboardingPage.vue'),
                name: 'onboarding',
                meta: { authenticated: false }
            },
            {
                path: '/auth/login',
                component: () => import('@/pages/LoginPage.vue'),
                name: 'login',
                meta: { authenticated: false }
            }
        ]
    },

    // Main
    {
        path: '/',
        component: () => import('@/components/layout/MainLayout.vue'),
        meta: { authenticated: true },
        children: [
            {
                path: '',
                component: () => import('@/pages/DashboardPage.vue'),
                name: 'dashboard'
            },
            {
                path: '/shows',
                component: () => import('@/pages/ShowPage.vue'),
                name: 'shows'
            },
            {
                path: '/movies',
                component: () => import('@/pages/MoviePage.vue'),
                name: 'movies'
            },
            {
                path: '/translations',
                component: () => import('@/pages/TranslationPage.vue'),
                name: 'translations'
            },
            {
                path: '/translations/:id',
                component: () => import('@/pages/TranslationDetailPage.vue'),
                name: 'translation-detail',
                props: true
            },
            {
                path: '/settings',
                component: () => import('@/pages/SettingPage.vue'),
                name: 'settings',
                redirect: { name: 'connections-media-settings' },
                children: [
                    {
                        path: 'connections/media-servers',
                        name: 'connections-media-settings',
                        component: () => import('@/pages/settings/IntegrationPage.vue')
                    },
                    {
                        path: 'connections/path-mapping',
                        name: 'connections-mapping-settings',
                        component: () => import('@/pages/settings/MappingPage.vue')
                    },
                    {
                        path: 'translation/setup',
                        name: 'translation-setup-settings',
                        component: () => import('@/pages/settings/ServicesPage.vue')
                    },
                    {
                        path: 'translation/subtitles',
                        name: 'translation-subtitles-settings',
                        component: () => import('@/pages/settings/SubtitlePage.vue')
                    },
                    {
                        path: 'translation/advanced',
                        name: 'translation-advanced-settings',
                        component: () => import('@/pages/settings/TranslationAdvancedPage.vue')
                    },
                    {
                        path: 'translation/prompts',
                        name: 'translation-prompts-settings',
                        component: () => import('@/pages/settings/TranslationPromptsPage.vue')
                    },
                    {
                        path: 'translation/request-template/:service',
                        name: 'request-template-settings',
                        component: () => import('@/pages/settings/RequestTemplatePage.vue'),
                        props: true
                    },
                    {
                        path: 'automation',
                        name: 'automation-settings',
                        component: () => import('@/pages/settings/AutomationPage.vue')
                    },
                    {
                        path: 'system/access',
                        name: 'system-access-settings',
                        component: () => import('@/pages/settings/AuthenticationPage.vue')
                    },
                    {
                        path: 'system/tasks',
                        name: 'system-tasks-settings',
                        component: () => import('@/pages/settings/SchedulePage.vue')
                    },
                    {
                        path: 'system/logs',
                        name: 'system-logs-settings',
                        component: () => import('@/pages/settings/LogsPage.vue')
                    },
                    {
                        path: 'plugins',
                        name: 'plugins-settings',
                        component: () => import('@/pages/settings/PluginsPage.vue')
                    },
                    {
                        path: 'plugin/:section/:tabId',
                        name: 'plugin-panel-settings',
                        component: () => import('@/pages/settings/PluginPanelPage.vue')
                    },
                    {
                        path: 'integration',
                        name: 'integration-settings',
                        redirect: { name: 'connections-media-settings' }
                    },
                    {
                        path: 'mapping',
                        name: 'mapping-settings',
                        redirect: { name: 'connections-mapping-settings' }
                    },
                    {
                        path: 'services',
                        name: 'services-settings',
                        redirect: { name: 'translation-setup-settings' }
                    },
                    {
                        path: 'subtitle',
                        name: 'subtitle-settings',
                        redirect: { name: 'translation-subtitles-settings' }
                    },
                    {
                        path: 'authentication',
                        name: 'authentication-settings',
                        redirect: { name: 'system-access-settings' }
                    },
                    {
                        path: 'tasks',
                        name: 'tasks-settings',
                        redirect: { name: 'system-tasks-settings' }
                    },
                    {
                        path: 'logs',
                        name: 'logs-settings',
                        redirect: { name: 'system-logs-settings' }
                    },
                    {
                        path: 'request-template/:service',
                        name: 'legacy-request-template-settings',
                        redirect: (to) => ({
                            name: 'request-template-settings',
                            params: { service: to.params.service }
                        })
                    }
                ]
            }
        ]
    }
]

const router = createRouter({
    history: createWebHistory(baseUrl()),
    routes
})

const staleAssetRecoveryKey = 'lingarr:stale-asset-recovery'
const staleAssetRecoveryWindowMs = 60_000
let staleAssetRecoveryStarted = false
let pendingNavigationHref = window.location.href

const isStaleAssetError = (error: unknown) => {
    const message = error instanceof Error ? error.message : String(error)
    return /dynamically imported module|failed to fetch.*module|importing a module script failed|unable to preload css/i.test(
        message
    )
}

const recoverFromStaleAsset = (error: unknown, targetHref = pendingNavigationHref) => {
    if (staleAssetRecoveryStarted || !isStaleAssetError(error)) return false

    try {
        const previousAttempt = Number(sessionStorage.getItem(staleAssetRecoveryKey))
        if (
            Number.isFinite(previousAttempt) &&
            previousAttempt > 0 &&
            Date.now() - previousAttempt < staleAssetRecoveryWindowMs
        )
            return false
        sessionStorage.setItem(staleAssetRecoveryKey, Date.now().toString())
    } catch {
        // Reloading without a persisted guard could create an endless refresh loop.
        return false
    }

    staleAssetRecoveryStarted = true
    window.location.assign(targetHref)
    return true
}

router.onError((error, to) => {
    const targetHref = to ? router.resolve(to).href : pendingNavigationHref
    if (!recoverFromStaleAsset(error, targetHref)) console.error('[Lingarr Next router]', error)
})

window.addEventListener('vite:preloadError', (event) => {
    const error = (event as Event & { payload?: unknown }).payload
    if (!isStaleAssetError(error)) return
    event.preventDefault()
    recoverFromStaleAsset(error)
})

router.isReady().then(() => {
    try {
        sessionStorage.removeItem(staleAssetRecoveryKey)
    } catch {
        // Storage may be unavailable in locked-down browser contexts.
    }
})

router.beforeEach(async (to) => {
    pendingNavigationHref = router.resolve(to).href

    if (!to.meta.authenticated) {
        return true
    }

    const instanceStore = useInstanceStore()
    const isAuthenticated = await instanceStore.ensureAuthenticated()

    if (!isAuthenticated && to.name !== 'login') {
        return { name: 'login' }
    }

    return true
})

export default router
