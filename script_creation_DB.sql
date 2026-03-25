-- Удаление тестовых login-ролей, создаваемых данным скриптом
DO $$
DECLARE
    v_login text;
BEGIN
    FOREACH v_login IN ARRAY ARRAY['ivan', 'viktor', 'nikita']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = v_login) THEN
            BEGIN
                EXECUTE format('REVOKE pd_employee FROM %I', v_login);
            EXCEPTION
                WHEN undefined_object THEN
            END;

            BEGIN
                EXECUTE format('REVOKE pd_admin FROM %I', v_login);
            EXCEPTION
                WHEN undefined_object THEN
            END;

            BEGIN
                EXECUTE format('REVOKE pd_superadmin FROM %I', v_login);
            EXCEPTION
                WHEN undefined_object THEN
            END;

            EXECUTE format('DROP ROLE %I', v_login);
        END IF;
    END LOOP;
END $$;

-- Полное удаление схемы приложения
DROP SCHEMA IF EXISTS product_dossier CASCADE;

-- Подключение расширения для crypt/gen_salt
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Создание схемы приложения
CREATE SCHEMA product_dossier;

-- Установка search_path
SET search_path TO product_dossier;

-- Создание перечисления статусов изделий
CREATE TYPE product_dossier.product_status_enum AS ENUM (
    'В_работе',
    'Завершён',
    'Архив',
    'В_корзине'
);

-- Создание перечисления статусов документов
CREATE TYPE product_dossier.document_status_enum AS ENUM (
    'В_работе',
    'Корректировка',
    'Завершён',
    'Архив',
    'В_корзине'
);

-- Создание перечисления операций истории
CREATE TYPE product_dossier.history_operation_enum AS ENUM (
    'Добавление',
    'Редактирование',
    'Изменение_категории_документа',
    'Изменение_данных_документа',
    'Перемещение_в_корзину',
    'Восстановление',
    'Окончательное_удаление'
);

-- Создание таблицы users
CREATE TABLE product_dossier.users (
    id_user BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    login VARCHAR NOT NULL UNIQUE,
    surname VARCHAR NOT NULL,
    name VARCHAR NOT NULL,
    patronymic VARCHAR
);

-- Создание таблицы products
CREATE TABLE product_dossier.products (
    id_product BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_number VARCHAR NOT NULL UNIQUE,
    name_product VARCHAR NOT NULL UNIQUE,
    description_product TEXT,
    status product_dossier.product_status_enum NOT NULL,
    status_before_delete product_dossier.product_status_enum,
    created_at TIMESTAMP NOT NULL,
    deleted_at TIMESTAMP,
    deleted_by BIGINT,
    CONSTRAINT fk_products_deleted_by
        FOREIGN KEY (deleted_by) REFERENCES product_dossier.users (id_user)
);

-- Создание таблицы document_categories
CREATE TABLE product_dossier.document_categories (
    id_document_category BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name_document_category VARCHAR NOT NULL UNIQUE,
    description_document_category TEXT,
    sort_order INTEGER NOT NULL UNIQUE CHECK (sort_order >= 0)
);

-- Создание таблицы documents
CREATE TABLE product_dossier.documents (
    id_document BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_document_category BIGINT NOT NULL,
    id_parent_document BIGINT,
    id_responsible_user BIGINT NOT NULL,
    document_number VARCHAR NOT NULL UNIQUE,
    name_document VARCHAR NOT NULL UNIQUE,
    description_document TEXT,
    status product_dossier.document_status_enum NOT NULL,
    status_before_delete product_dossier.document_status_enum,
    deleted_at TIMESTAMP,
    deleted_by BIGINT,
    is_deleted_root BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT fk_documents_document_category
        FOREIGN KEY (id_document_category) REFERENCES product_dossier.document_categories (id_document_category),
    CONSTRAINT fk_documents_parent_document
        FOREIGN KEY (id_parent_document) REFERENCES product_dossier.documents (id_document),
    CONSTRAINT fk_documents_responsible_user
        FOREIGN KEY (id_responsible_user) REFERENCES product_dossier.users (id_user),
    CONSTRAINT fk_documents_deleted_by
        FOREIGN KEY (deleted_by) REFERENCES product_dossier.users (id_user)
);

-- Создание таблицы product_documents
CREATE TABLE product_dossier.product_documents (
    id_product BIGINT NOT NULL,
    id_document BIGINT NOT NULL,
    CONSTRAINT pk_product_documents PRIMARY KEY (id_product, id_document),
    CONSTRAINT fk_product_documents_product
        FOREIGN KEY (id_product) REFERENCES product_dossier.products (id_product),
    CONSTRAINT fk_product_documents_document
        FOREIGN KEY (id_document) REFERENCES product_dossier.documents (id_document)
);

-- Создание таблицы files
CREATE TABLE product_dossier.files (
    id_file BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_document BIGINT NOT NULL,
    id_uploaded_by BIGINT NOT NULL,
    file_name VARCHAR NOT NULL,
    file_path VARCHAR NOT NULL,
    recycle_bin_file_path VARCHAR,
    file_extension VARCHAR NOT NULL,
    file_size_bytes BIGINT NOT NULL CHECK (file_size_bytes >= 0),
    uploaded_at TIMESTAMP NOT NULL,
    last_modified_at TIMESTAMP NOT NULL,
    CONSTRAINT fk_files_document
        FOREIGN KEY (id_document) REFERENCES product_dossier.documents (id_document) ON DELETE CASCADE,
    CONSTRAINT fk_files_uploaded_by
        FOREIGN KEY (id_uploaded_by) REFERENCES product_dossier.users (id_user)
);

-- Создание таблицы document_change_history
CREATE TABLE product_dossier.document_change_history (
    id_change BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_surname VARCHAR NOT NULL,
    user_name VARCHAR NOT NULL,
    user_patronymic VARCHAR,
    operation product_dossier.history_operation_enum NOT NULL,
    changed_at TIMESTAMP NOT NULL,
    file_name VARCHAR NOT NULL,
    file_path VARCHAR NOT NULL,
    product_number VARCHAR NOT NULL,
    product_name VARCHAR NOT NULL,
    document_number VARCHAR NOT NULL,
    document_name VARCHAR NOT NULL
);

-- Создание индексов для ускорения работы Recycle bin
CREATE INDEX idx_products_status_deleted_at
    ON product_dossier.products (status, deleted_at);

CREATE INDEX idx_documents_status_deleted_at_root
    ON product_dossier.documents (status, deleted_at, is_deleted_root);

CREATE INDEX idx_documents_parent_document
    ON product_dossier.documents (id_parent_document);

CREATE INDEX idx_files_id_document
    ON product_dossier.files (id_document);

-- Создание групповых ролей приложения
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pd_employee') THEN
        CREATE ROLE pd_employee NOLOGIN;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pd_admin') THEN
        CREATE ROLE pd_admin NOLOGIN;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pd_superadmin') THEN
        CREATE ROLE pd_superadmin NOLOGIN;
    END IF;
END $$;

-- Настройка наследования ролей приложения
GRANT pd_employee TO pd_admin;
GRANT pd_admin TO pd_superadmin;

-- Выдача прав на схему
GRANT USAGE ON SCHEMA product_dossier TO pd_employee, pd_admin, pd_superadmin;

-- Сотрудник: просмотр и изменение данных без физического удаления
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA product_dossier TO pd_employee;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA product_dossier TO pd_employee;

-- Администратор: дополнительные DELETE-права не выдаются, так как удаление выполняется через Recycle bin
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA product_dossier TO pd_admin;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA product_dossier TO pd_admin;

-- Супер-администратор: полный доступ
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA product_dossier TO pd_superadmin;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA product_dossier TO pd_superadmin;

-- Права по умолчанию
ALTER DEFAULT PRIVILEGES IN SCHEMA product_dossier
    GRANT SELECT, INSERT, UPDATE ON TABLES TO pd_employee;

ALTER DEFAULT PRIVILEGES IN SCHEMA product_dossier
    GRANT USAGE, SELECT ON SEQUENCES TO pd_employee;

ALTER DEFAULT PRIVILEGES IN SCHEMA product_dossier
    GRANT SELECT, INSERT, UPDATE ON TABLES TO pd_admin;

ALTER DEFAULT PRIVILEGES IN SCHEMA product_dossier
    GRANT USAGE, SELECT ON SEQUENCES TO pd_admin;

ALTER DEFAULT PRIVILEGES IN SCHEMA product_dossier
    GRANT ALL ON TABLES TO pd_superadmin;

ALTER DEFAULT PRIVILEGES IN SCHEMA product_dossier
    GRANT ALL ON SEQUENCES TO pd_superadmin;

-- Создание процедуры регистрации пользователя
CREATE OR REPLACE PROCEDURE product_dossier.register_user(
    IN p_login varchar,
    IN p_password varchar,
    IN p_surname varchar,
    IN p_name varchar,
    IN p_patronymic varchar DEFAULT NULL
)
LANGUAGE plpgsql
AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM product_dossier.users u WHERE u.login = p_login) THEN
        RAISE EXCEPTION 'Пользователь с таким логином уже существует';
    END IF;

    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = p_login) THEN
        RAISE EXCEPTION 'DB-пользователь с таким логином уже существует';
    END IF;

    INSERT INTO product_dossier.users (login, surname, name, patronymic)
    VALUES (p_login, p_surname, p_name, p_patronymic);

    EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', p_login, p_password);
    EXECUTE format('GRANT pd_employee TO %I', p_login);
END;
$$;

-- Создание процедуры смены роли пользователя
CREATE OR REPLACE PROCEDURE product_dossier.set_user_db_role(
    IN p_target_login varchar,
    IN p_new_role_name varchar
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, product_dossier
AS $$
DECLARE
    v_target_is_superadmin boolean;
BEGIN
    IF session_user <> 'postgres'
       AND NOT pg_has_role(session_user, 'pd_superadmin', 'member') THEN
        RAISE EXCEPTION 'Недостаточно прав: требуется роль pd_superadmin';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = p_target_login) THEN
        RAISE EXCEPTION 'DB-пользователь не найден';
    END IF;

    IF p_new_role_name NOT IN ('pd_employee', 'pd_admin', 'pd_superadmin') THEN
        RAISE EXCEPTION 'Некорректная роль';
    END IF;

    SELECT pg_has_role(p_target_login, 'pd_superadmin', 'member')
      INTO v_target_is_superadmin;

    IF session_user <> 'postgres'
       AND v_target_is_superadmin
       AND p_new_role_name <> 'pd_superadmin'
       AND p_target_login <> session_user THEN
        RAISE EXCEPTION
            'Нельзя забрать роль pd_superadmin у другого пользователя. Пользователь может снять эту роль только сам у себя.';
    END IF;

    EXECUTE format('REVOKE pd_employee FROM %I', p_target_login);
    EXECUTE format('REVOKE pd_admin FROM %I', p_target_login);
    EXECUTE format('REVOKE pd_superadmin FROM %I', p_target_login);
    EXECUTE format('GRANT %I TO %I', p_new_role_name, p_target_login);
END;
$$;

-- Создание процедуры смены пароля пользователя
CREATE OR REPLACE PROCEDURE product_dossier.change_user_password(
    IN p_target_login varchar,
    IN p_new_password varchar
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, product_dossier
AS $$
DECLARE
    v_target_is_superadmin boolean;
BEGIN
    IF session_user <> 'postgres'
       AND NOT pg_has_role(session_user, 'pd_superadmin', 'member') THEN
        RAISE EXCEPTION 'Недостаточно прав: требуется роль pd_superadmin';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = p_target_login) THEN
        RAISE EXCEPTION 'DB-пользователь не найден';
    END IF;

    SELECT pg_has_role(p_target_login, 'pd_superadmin', 'member')
      INTO v_target_is_superadmin;

    IF session_user <> 'postgres'
       AND v_target_is_superadmin
       AND p_target_login <> session_user THEN
        RAISE EXCEPTION 'Нельзя изменить пароль другого пользователя с ролью pd_superadmin';
    END IF;

    EXECUTE format('ALTER ROLE %I WITH PASSWORD %L', p_target_login, p_new_password);
END;
$$;

-- Назначение владельца процедур
ALTER PROCEDURE product_dossier.register_user(varchar, varchar, varchar, varchar, varchar) OWNER TO postgres;
ALTER PROCEDURE product_dossier.set_user_db_role(varchar, varchar) OWNER TO postgres;
ALTER PROCEDURE product_dossier.change_user_password(varchar, varchar) OWNER TO postgres;

-- Очистка прав PUBLIC на процедуры
REVOKE ALL ON PROCEDURE product_dossier.register_user(varchar, varchar, varchar, varchar, varchar) FROM PUBLIC;
REVOKE ALL ON PROCEDURE product_dossier.set_user_db_role(varchar, varchar) FROM PUBLIC;
REVOKE ALL ON PROCEDURE product_dossier.change_user_password(varchar, varchar) FROM PUBLIC;

-- Выдача прав на выполнение процедур
GRANT EXECUTE ON PROCEDURE product_dossier.register_user(varchar, varchar, varchar, varchar, varchar) TO postgres;
GRANT EXECUTE ON PROCEDURE product_dossier.set_user_db_role(varchar, varchar) TO pd_superadmin, postgres;
GRANT EXECUTE ON PROCEDURE product_dossier.change_user_password(varchar, varchar) TO pd_superadmin, postgres;