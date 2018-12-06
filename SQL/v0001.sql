drop proc `check_solution_begin`;

DELIMITER //
CREATE DEFINER=`root`@`localhost` PROCEDURE `check_solution_begin`()
    SQL SECURITY INVOKER
BEGIN
    update
        solution
    set
        state = 'checking'
    where
        state = 'submitted'
        and id = LAST_INSERT_ID(id)
    order by
        id
    limit 1;

    select 
        (
            select
                JSON_REMOVE(JSON_OBJECT(
                    'id', s.id,
                    'source', s.source,
                    'time', p.time_limit,
                    'memory', p.memory_limit,
                    'customChecker', p.custom_checker,
                    'tests', JSON_ARRAYAGG(
                        JSON_OBJECT('id', id, 'input', input, 'output', output)
                    )
                ), if(p.custom_checker is null, '$.customChecker', '$.dummy')
                ) result
            from test t
            where t.problem = s.problem
        ) tests
    from solution s 
    join problem p on p.id = s.problem
    where s.id = LAST_INSERT_ID();
END//
DELIMITER ;