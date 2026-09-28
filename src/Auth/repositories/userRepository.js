const db = require("../db");

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
            created_at
        FROM users
        WHERE username = $1
        `,
        [username]
    );

    return result.rows[0] || null;
}

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
            created_at
        FROM users
        WHERE email = $1
        `,
        [email]
    );

    return result.rows[0] || null;
}

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
            created_at
        `,
        [username, email, passwordHash, role]
    );

    return result.rows[0];
}

module.exports = {
    getUserByUsername,
    getUserByEmail,
    createUser
};