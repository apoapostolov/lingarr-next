import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { ILogQuery, ILogsService } from '@/ts'
import { resolveUrl } from '@/utils/baseUrl'

const service = (http: AxiosStatic, resource = '/api/logs'): ILogsService => ({
    getPage<T>(query: ILogQuery = {}): Promise<T> {
        const params = new URLSearchParams()
        if (query.limit) {
            params.set('limit', String(query.limit))
        }
        if (query.before) {
            params.set('before', String(query.before))
        }
        if (query.after) {
            params.set('after', String(query.after))
        }
        if (query.minLevel) {
            params.set('minLevel', query.minLevel)
        }
        const queryString = params.toString()
        const suffix = queryString ? `?${queryString}` : ''
        return new Promise((resolve, reject) => {
            http.get(`${resource}${suffix}`)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },

    clear(): Promise<void> {
        return new Promise((resolve, reject) => {
            http.delete(resource)
                .then(() => {
                    resolve()
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },

    getStream(after: number, minLevel?: string): EventSource {
        const params = new URLSearchParams({ after: String(after) })
        if (minLevel) {
            params.set('minLevel', minLevel)
        }
        return new EventSource(resolveUrl(`${resource}/stream?${params}`))
    }
})

export const logsService = (http: AxiosStatic): ILogsService => {
    return service(http)
}
