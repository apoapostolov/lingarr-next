import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import {
    IPluginManifest,
    IPluginOptionsResponse,
    IPluginStatus,
    IPluginSummary,
    IPluginUi,
    IPluginService
} from '@/ts'

const service = (http: AxiosStatic, resource = '/api/plugin'): IPluginService => ({
    list(): Promise<IPluginSummary[]> {
        return new Promise((resolve, reject) => {
            http.get(resource)
                .then((response: AxiosResponse<IPluginSummary[]>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    getManifest(provider: string): Promise<IPluginManifest> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/${provider}/manifest`)
                .then((response: AxiosResponse<IPluginManifest>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    getStatus(provider: string): Promise<IPluginStatus> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/${provider}/status`)
                .then((response: AxiosResponse<IPluginStatus>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    getOptions(endpoint: string): Promise<IPluginOptionsResponse> {
        return new Promise((resolve, reject) => {
            http.get(endpoint)
                .then((response: AxiosResponse<IPluginOptionsResponse>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    ui(): Promise<IPluginUi> {
        return http.get(`${resource}/ui`).then((response) => response.data)
    },
    values(provider: string): Promise<Record<string, string>> {
        return http
            .get(`${resource}/${provider}/settings`)
            .then((response) => response.data.values ?? {})
    },
    saveValues(provider: string, values: Record<string, string>): Promise<string> {
        return http
            .put(`${resource}/${provider}/settings`, { values })
            .then((response) => response.data.message as string)
    },
    saveHost(provider, host): Promise<string> {
        return http
            .put(`${resource}/${provider}/host`, host)
            .then((response) => response.data.message as string)
    },
    runAction(provider: string, action: string): Promise<string> {
        return http
            .post(`${resource}/${provider}/actions/${action}`)
            .then((response) => response.data.message as string)
    }
})

export const pluginService = (axios: AxiosStatic): IPluginService => {
    return service(axios)
}
