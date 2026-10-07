import { useCallback, useEffect, useRef, useState } from 'react'
import EmptyState from './EmptyState.jsx'
import {
  createHolding,
  deleteHolding,
  fetchManageableHoldings,
} from '../lib/portfolioApi.js'
import { UNKNOWN, formatDateTime, formatMaybeCurrency, formatMaybeNumber } from '../lib/format.js'

const EMPTY_FORM = { symbol: '', quantity: '', averageCost: '', account: '' }

/**
 * Parses a user-typed number, tolerating thousands separators and stray spaces.
 * Returns undefined for blank (an omitted optional value) and null for unparseable.
 */
function parseNumber(text) {
  const trimmed = String(text ?? '').trim()
  if (!trimmed) return undefined

  const cleaned = trimmed.replace(/[$,\s]/g, '')
  if (!/^\d*\.?\d+$/.test(cleaned)) return null

  const value = Number(cleaned)
  return Number.isFinite(value) ? value : null
}

export default function ManualEntry({ onChanged }) {
  const [form, setForm] = useState(EMPTY_FORM)
  const [error, setError] = useState(null)
  const [result, setResult] = useState(null)
  const [isSaving, setIsSaving] = useState(false)
  const [holdings, setHoldings] = useState(null)
  const [listError, setListError] = useState(null)
  const [removingId, setRemovingId] = useState(null)
  const inFlight = useRef(false)
  const symbolRef = useRef(null)

  const load = useCallback(async (signal) => {
    try {
      const rows = await fetchManageableHoldings({ signal })
      if (signal?.aborted) return
      setHoldings(rows)
      setListError(null)
    } catch (cause) {
      if (cause.name === 'AbortError' || signal?.aborted) return
      setListError(cause.message ?? 'Could not load positions.')
      setHoldings([])
    }
  }, [])

  // Synchronising with the API, and aborting on unmount so a slow response cannot set state on
  // a component that is already gone. The lint rule cannot see that `load` only sets state after
  // an await, which is the "synchronising with an external system" case it explicitly allows.
  useEffect(() => {
    const controller = new AbortController()
    // oxlint-disable-next-line set-state-in-effect
    load(controller.signal)
    return () => controller.abort()
  }, [load])

  const update = (field) => (event) =>
    setForm((current) => ({ ...current, [field]: event.target.value }))

  async function handleSubmit(event) {
    event.preventDefault()
    // State updates are batched, so an isSaving check alone would let two rapid submits through.
    if (inFlight.current) return

    const quantity = parseNumber(form.quantity)
    if (quantity === null) {
      setError('Number of shares must be a number.')
      return
    }
    if (quantity === undefined) {
      setError('Number of shares is required.')
      return
    }

    const averageCost = parseNumber(form.averageCost)
    if (averageCost === null) {
      setError('Average cost must be a number, or left blank.')
      return
    }

    inFlight.current = true
    setIsSaving(true)
    setError(null)
    setResult(null)

    try {
      const saved = await createHolding({
        symbol: form.symbol,
        quantity,
        averageCost: averageCost ?? null,
        account: form.account.trim() || null,
      })

      setResult(saved)
      setForm((current) => ({ ...EMPTY_FORM, account: current.account }))
      symbolRef.current?.focus()
      await load()
      onChanged?.()
    } catch (cause) {
      setError(cause.message ?? 'Could not save the position.')
    } finally {
      inFlight.current = false
      setIsSaving(false)
    }
  }

  async function handleRemove(holding) {
    setRemovingId(holding.id)
    setError(null)
    setResult(null)
    try {
      await deleteHolding(holding.id)
      await load()
      onChanged?.()
    } catch (cause) {
      setError(cause.message ?? 'Could not remove the position.')
    } finally {
      setRemovingId(null)
    }
  }

  return (
    <>
      <section className="panel" aria-label="Add a position manually">
        <div className="panel__head">
          <h2 className="panel__title">Add a position</h2>
          <span className="panel__hint">Symbol and shares are required</span>
        </div>

        <form className="manual__form" onSubmit={handleSubmit} noValidate>
          <label className="field manual__field">
            <span className="field__label">Symbol *</span>
            <input
              ref={symbolRef}
              className="field__input"
              name="symbol"
              placeholder="MSFT"
              autoComplete="off"
              autoCapitalize="characters"
              spellCheck="false"
              required
              value={form.symbol}
              onChange={update('symbol')}
              disabled={isSaving}
            />
          </label>

          <label className="field manual__field">
            <span className="field__label">Shares *</span>
            <input
              className="field__input"
              name="quantity"
              inputMode="decimal"
              placeholder="10"
              autoComplete="off"
              required
              value={form.quantity}
              onChange={update('quantity')}
              disabled={isSaving}
            />
          </label>

          <label className="field manual__field">
            <span className="field__label">Average cost</span>
            <input
              className="field__input"
              name="averageCost"
              inputMode="decimal"
              placeholder="optional"
              autoComplete="off"
              value={form.averageCost}
              onChange={update('averageCost')}
              disabled={isSaving}
            />
          </label>

          <label className="field manual__field">
            <span className="field__label">Account</span>
            <input
              className="field__input"
              name="account"
              placeholder="Manual"
              autoComplete="off"
              value={form.account}
              onChange={update('account')}
              disabled={isSaving}
            />
          </label>

          <button className="button button--primary manual__submit" type="submit" disabled={isSaving}>
            {isSaving ? 'Adding…' : 'Add position'}
          </button>
        </form>

        <p className="panel__footnote">
          Leave average cost blank if you do not track it — unrealized gain is reported as unknown
          rather than guessed. Re-adding a symbol you already hold updates its share count and
          keeps any cost you entered before. The symbol is added to your watchlist so the
          monitoring run starts fetching prices for it.
        </p>
      </section>

      {error ? (
        <div className="banner banner--error" role="alert">
          <span>{error}</span>
        </div>
      ) : null}

      {result ? (
        <div className="banner banner--success" role="status">
          <div>
            <strong>
              {result.created ? 'Added' : 'Updated'} {result.symbol}
            </strong>{' '}
            in {result.account}
            {result.addedToWatchlist ? ' · now on your watchlist' : ''}.
          </div>
        </div>
      ) : null}

      <section className="panel" aria-label="Current positions">
        <div className="panel__head">
          <h2 className="panel__title">Current positions</h2>
          <span className="panel__hint">
            {holdings === null ? 'Loading…' : `${holdings.length} stored`}
          </span>
        </div>

        {listError ? (
          <EmptyState title="Could not load positions">{listError}</EmptyState>
        ) : holdings === null ? (
          <p className="subtle">Loading…</p>
        ) : holdings.length === 0 ? (
          <EmptyState title="No positions stored">
            Add one above, or upload a broker positions file.
          </EmptyState>
        ) : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Symbol</th>
                  <th scope="col" className="num">Shares</th>
                  <th scope="col" className="num">Avg cost</th>
                  <th scope="col">Source</th>
                  <th scope="col" className="num">Updated</th>
                  <th scope="col"><span className="visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                {holdings.map((holding) => (
                  <tr key={holding.id}>
                    <th scope="row">
                      <span className="symbol">
                        {holding.symbol}
                        {holding.isMonitorable ? null : (
                          <span className="chip chip--ok symbol__tag">{holding.positionType}</span>
                        )}
                      </span>
                      <span className="subtle">
                        {holding.description ?? 'No description'} · {holding.account}
                      </span>
                    </th>
                    <td className="num">{formatMaybeNumber(holding.quantity, 4)}</td>
                    <td className="num">{formatMaybeCurrency(holding.averageCost)}</td>
                    <td>
                      <span className={`chip ${holding.isManual ? 'chip--alert' : 'chip--ok'}`}>
                        {holding.isManual ? 'Manual' : 'Imported'}
                      </span>
                      {holding.sourceFile ? (
                        <span className="subtle" title={holding.sourceFile}>
                          {holding.sourceFile}
                        </span>
                      ) : null}
                    </td>
                    <td className="num">
                      {holding.updatedAt ? formatDateTime(holding.updatedAt) : UNKNOWN}
                    </td>
                    <td>
                      <button
                        type="button"
                        className="button button--ghost"
                        onClick={() => handleRemove(holding)}
                        disabled={removingId === holding.id}
                      >
                        {removingId === holding.id ? 'Removing…' : 'Remove'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <p className="panel__footnote">
          Removing a position deletes it from your portfolio but keeps the symbol on the watchlist,
          so you can still track a stock you no longer own.
        </p>
      </section>
    </>
  )
}
