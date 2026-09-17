-- M5d: a slot the vehicle could not reach at all, as distinct from one it reached badly (needs_adjust).
ALTER TABLE parking_slot DROP CONSTRAINT parking_slot_status_check;
ALTER TABLE parking_slot ADD CONSTRAINT parking_slot_status_check
  CHECK (status IN ('empty', 'filled', 'needs_adjust', 'unreachable'));
