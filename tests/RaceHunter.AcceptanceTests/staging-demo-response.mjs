/**
 * Start consuming the response body as soon as Playwright resolves the
 * response promise. This keeps SPA navigation from discarding the CDP body.
 *
 * @param {Promise<{ body(): Promise<Buffer> }>} responsePromise
 * @returns {Promise<any>}
 */
export async function bufferJsonResponse(responsePromise) {
  const response = await responsePromise
  const body = await response.body()
  return JSON.parse(body.toString('utf8'))
}
