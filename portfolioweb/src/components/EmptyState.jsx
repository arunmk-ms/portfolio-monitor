/**
 * Explains why a panel has nothing to show.
 *
 * An empty dashboard panel is ambiguous — it could mean "no data yet" or "something broke". The
 * dashboard reads real stored data now, so each panel says which step still has to happen.
 */
export default function EmptyState({ title, children, action }) {
  return (
    <div className="empty">
      <p className="empty__title">{title}</p>
      {children ? <p className="empty__body">{children}</p> : null}
      {action ?? null}
    </div>
  )
}
