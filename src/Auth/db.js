const path = require("path");
const dotenv = require("dotenv");
const { Client } = require("pg");

dotenv.config({
    path: path.resolve(__dirname, "../../.env")
});

const db = new Client({
    host: process.env.POSTGRES_HOST,
    port: Number(process.env.POSTGRES_PORT),
    database: process.env.POSTGRES_DB,
    user: process.env.POSTGRES_USER,
    password: process.env.POSTGRES_PASSWORD
});

module.exports = db;