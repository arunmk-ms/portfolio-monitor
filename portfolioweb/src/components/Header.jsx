import Nav from './Nav.jsx'

function initialsOf(name = '') {
  return (
    name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0].toUpperCase())
      .join('') || '?'
  )
}

export default function Header({ user, onSignOut, route, title, children }) {
  return (
    <header className="header">
      <div className="header__top">
        <div>
          <p className="header__eyebrow">Portfolio Monitor</p>
          <h1 className="header__title">{title}</h1>
        </div>

        <Nav route={route} />

        <div className="header__account">
          <div className="account">
            <span className="account__avatar" aria-hidden="true">
              {initialsOf(user.displayName)}
            </span>
            <span className="account__text">
              <span className="account__name">{user.displayName}</span>
              <span className="account__email">{user.email}</span>
            </span>
          </div>
          <button type="button" className="button button--ghost" onClick={onSignOut}>
            Sign out
          </button>
        </div>
      </div>

      {children ? <div className="header__bar">{children}</div> : null}
    </header>
  )
}
