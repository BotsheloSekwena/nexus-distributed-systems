
const db = require("../db");

/**
 * Retrieves a user by username.
 */
async function getUserByUsername(username) {
    const result = await db.query(
        `
        SELECT
            id,
            username,
            email,
            password_hash,
            role,
            is_active,
            token_version,
            created_at
        FROM users
        WHERE username = $1
        `,
        [username]
    );

    return result.rows[0] || null;
}

/**
 * Retrieves a user by email.
 */
async function getUserByEmail(email) {
    const result = await db.query(
        `
        SELECT
            id,
            username,
            email,
            password_hash,
            role,
            is_active,
            token_version,
            created_at
        FROM users
        WHERE email = $1
        `,
        [email]
    );

    return result.rows[0] || null;
}

/**
 * Creates a new user account.
 */
async function createUser({
    username,
    email,
    passwordHash,
    role = "User"
}) {
    const result = await db.query(
        `
        INSERT INTO users (
            username,
            email,
            password_hash,
            role
        )
        VALUES ($1, $2, $3, $4)
        RETURNING
            id,
            username,
            email,
            role,
            is_active,
            token_version,
            created_at
        `,
        [username, email, passwordHash, role]
    );

    return result.rows[0];
}

/**
 * Checks whether a JWT still matches the current account state.
 */
async function isTokenValid(userId, tokenVersion, role) {
    const result = await db.query(
        `
        SELECT EXISTS (
            SELECT 1
            FROM users
            WHERE id = $1
              AND token_version = $2
              AND role = $3
              AND is_active = TRUE
        ) AS valid
        `,
        [userId, tokenVersion, role]
    );

    return result.rows[0].valid;
}

module.exports = {
    getUserByUsername,
    getUserByEmail,
    createUser,
    isTokenValid
};