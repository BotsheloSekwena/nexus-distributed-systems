
BEGIN;

-- Add a version number to each user account.
ALTER TABLE users
ADD COLUMN IF NOT EXISTS token_version INTEGER NOT NULL DEFAULT 0;

-- Reject negative token versions.
ALTER TABLE users
ADD CONSTRAINT users_token_version_nonnegative
CHECK (token_version >= 0);

-- Increment the token version whenever security-sensitive
-- account information changes.
CREATE OR REPLACE FUNCTION increment_user_token_version()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.role IS DISTINCT FROM OLD.role
       OR NEW.is_active IS DISTINCT FROM OLD.is_active
       OR NEW.password_hash IS DISTINCT FROM OLD.password_hash THEN

        NEW.token_version := OLD.token_version + 1;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS users_token_version_trigger ON users;

CREATE TRIGGER users_token_version_trigger
BEFORE UPDATE OF role, is_active, password_hash
ON users
FOR EACH ROW
EXECUTE FUNCTION increment_user_token_version();

COMMIT;