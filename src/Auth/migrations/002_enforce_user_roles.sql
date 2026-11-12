BEGIN;

ALTER TABLE users
ADD CONSTRAINT users_role_allowed
CHECK (role IN ('User', 'Administrator'));

COMMIT;