import axios, { AxiosStatic } from 'axios'
import { Services } from '@/ts'
import { baseUrl } from '@/utils/baseUrl'
import { translationRequestService } from '@/services/translationRequestService'
import { authService } from './authService'
import { subtitleService } from './subtitleService'
import { translateService } from './translateService'
import { settingService } from './settingService'
import { mediaService } from './mediaService'
import { versionService } from './versionService'
import { scheduleService } from '@/services/scheduleService'
import { mappingService } from '@/services/mappingService'
import { directoryService } from '@/services/directoryService'
import { statisticsService } from '@/services/statisticsService'
import { logsService } from '@/services/logsService'
import { requestTemplateService } from '@/services/requestTemplateService'
import { pluginService } from '@/services/pluginService'
import { providerHealthService } from '@/services/providerHealthService'
import { dashboardService } from '@/services/dashboardService'
import { promptProfileService } from '@/services/promptProfileService'
import { xaiOAuthService } from '@/services/xaiOAuthService'
import { plexService } from '@/services/plexService'

axios.defaults.baseURL = baseUrl()

axios.interceptors.response.use(
    (response) => response,
    (error) => {
        const prefix = baseUrl().replace(/\/$/, '')
        const loginPath = `${prefix}/auth/login`
        const onboardingPath = `${prefix}/auth/onboarding`
        if (error.response?.status === 403 && error.response?.data?.onboardingRequired) {
            window.location.href = onboardingPath
            return
        }
        if (error.response?.status === 401) {
            const currentPath = window.location.pathname
            if (!currentPath.startsWith(loginPath) && !currentPath.startsWith(onboardingPath)) {
                window.location.href = loginPath
                return
            }
        }
        return Promise.reject(error)
    }
)

const services = (axios: AxiosStatic): Services => ({
    auth: authService(axios),
    setting: settingService(axios),
    subtitle: subtitleService(axios),
    translate: translateService(axios),
    translationRequest: translationRequestService(axios),
    version: versionService(axios),
    media: mediaService(axios),
    schedule: scheduleService(axios),
    mapping: mappingService(axios),
    directory: directoryService(axios),
    statistics: statisticsService(axios),
    logs: logsService(axios),
    requestTemplate: requestTemplateService(axios),
    plugin: pluginService(axios),
    xaiOAuth: xaiOAuthService(axios),
    plex: plexService(axios),
    providerHealth: providerHealthService(axios),
    dashboard: dashboardService(axios),
    promptProfile: promptProfileService(axios)
})

export default services(axios)
