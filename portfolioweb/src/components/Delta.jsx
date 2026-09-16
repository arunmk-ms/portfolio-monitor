import { directionOf } from '../lib/format.js'

const arrows = { up: '▲', down: '▼', flat: '■' }

export default function Delta({ value, children, showArrow = true }) {
  const direction = directionOf(value)

  return (
    <span className={`delta delta--${direction}`}>
      {showArrow ? <span className="delta__arrow">{arrows[direction]}</span> : null}
      {children}
    </span>
  )
}
